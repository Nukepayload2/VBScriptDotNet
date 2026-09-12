# VBScript.NET 问题登记（issues 层）

本文档登记 **VBScript.NET 产品自身**发现的 bug / 缺陷（issues 层）。与 `proposals/`（能力增强）、`meetings/`（会议评估）、`spec/`（规范）分离：issue 是**既存行为与预期不符**的记录，给出症状、根因、预期行为与修复方向。

## 目录组织

- `issues/` ↔ 根目录登记 Open / In Progress 的 bug。
- 修复验证通过后：在 issue 内标记 **Fixed** 并注明修复 commit；若涉及能力变更，转 `proposals/` 评估。

## 问题清单

| # | 文件 | 问题 | 状态 |
|---|------|------|------|
| 01 | `issue-vbx-load-span-shift.md` | vbx `#Load` 文本内联导致后续 TextSpan 漂移（预期：与 C# `#load` 一样零漂移，独立树合并） | **Fixed**（2026-08-15） |
| 02 | `issue-scripting-xml-linq-reference.md` | Scripting 63 失败：net10.0 把 Xml.Linq 移到 `System.Private.Xml.Linq`，`IncludeInternalXmlHelper` 嵌入 helper 树但绑定缺直接引用 → 每个脚本提交 24 个 BC30002 | **Fixed**（2026-08-20） |
| 03 | `issue-vbi-imports-switch-nre.md` | vbi `/imports:<非法值>` 抛 `NullReferenceException` 栈、`/imports:` 诊断丢失（`ParseGlobalImports` 把 `Nothing` 塞进 `GlobalImports`，宿主 `GetScriptOptions` 在 Errors 门之前触发 `GetImports` NRE） | **Fixed**（2026-09-10） |
| 04 | `issue-script-top-level-extension-method-crash.md` | 脚本顶层 `<Extension>` 成员漏写 `Shared` 时无编译诊断：Debug 构建被 `Debug.Assert(Me.IsShared)` 断言终止，Release 落到 codegen NRE（`AllowsExtensionMethods` 把脚本类与标准模块并列放行，而「扩展方法必须 `Shared`」在模块内不可能被违反，故从无用户可见诊断） | **Open** |
| 05 | `issue-submission-shared-field-initializer-typeload.md` | 提交里顶层 `Shared` 字段带初始化器 → 宿主 `TypeLoadException`（`IsSubmissionConstructor` 不看 `IsShared`，共享提交构造器被塞进含 `Me`/`submissionArray` 的实例初始化体）；编译期零诊断 | **Open** |
| 06 | `issue-script-shared-field-await-initializer-crash.md` | 脚本顶层 `Shared` 字段初始化器含 `Await` → 编译器断言终止（`AwaitOperator` 存活到 codegen，落 `EmitExpression.vb:206-209` 的 `Case Else` 抛 `UnexpectedValue`；`EXITCODE=35`）。**非共享**同形状正常（已运行对照）⇒ `Shared` 特有。VB 缺 C# CS8100「静态字段初始化器不许 `await`」的对应诊断 ⇒ 从「报错」变「崩」 | **Open** |
| 07 | `issue-submission-implicit-type-member-asserts.md` | 提交类顶层 `Event` / `WithEvents` 成员 → Debug 断言终止（提交类由 `ImplicitNamedTypeSymbol` 承载，`IsImplicitlyDeclared` 报 True，撞 `SynthesizedEventAccessorSymbol.vb:495` / `SourceWithEventsBackingFieldSymbol.vb:66` 的 `Not ContainingType.IsImplicitlyDeclared` 断言；`EXITCODE=35`）。**与 `Shared` 无关**（实例形状同样终止，已运行对照）；嵌套类里的同形状正常 | **Open** |
| 08 | `issue-submission-shared-member-implicit-me.md` | 提交类的共享成员不报 BC30369：隐式 `Me` 到实例成员 → 共享方法体编译零诊断、运行期 `InvalidProgramException`（`EXITCODE=58`）；共享字段初始化器同形状则先撞 issue 05。根因 `Binder_Expressions.vb:2257-2270` 以注释「No code in a script class is shared」为前提，对脚本类的**任何**隐式 `Me` 一律 `Return True`，永不走到 `:2272-2278` 的 BC30369。**普通类同形状报 BC30369**（已运行对照，两形状） | **Open** |
| 09 | `issue-initializer-diagnostic-does-not-gate-emit.md` | 初始化器里的「`Await` 不在 async 上下文」诊断（BC36937）**报了但不阻止发射**：嵌套类型（**与 `Shared` 无关**）里字段/属性初始化器含 `Await` → Debug 发射期断言终止（`EXITCODE=35`）且**零诊断输出**。根因：初始化器诊断经 `MethodCompiler.vb:599-607` 写进全局 `_diagnostics`，而逐方法发射门（`:1272` 的 `hasErrors`）只看 `diagsForCurrentMethod`（`GetInstance(template)` 的空袋，`BindingDiagnosticBag.vb:50-52`）与 bound 节点错误标志（`Binder_Expressions.vb:4742-4744` 只报不标）；判别性实证：同形状换 `Await 5`（操作数带错）就正常报 BC36937+BC36930、不崩 | **Open** |

> 状态约定：**Open**（待修复）/ **In Progress**（已认领）/ **Fixed**（已验证修复，注明 commit）。
