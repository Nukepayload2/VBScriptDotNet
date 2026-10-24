# 脚本顶层 `RaiseEvent` 被读成 Custom Event 的访问器声明，事件 raise 不可用

* 状态：**Fixed**（已验证，commit 待作者提交后补）
* 发现日期：2026-10-23
* 发现场景：关键字 × 顶层容器全量审计（`tasks\keyword-toplevel-audit\`）；同一处 switch 的 `End`（issue 39）与 `AddHandler`/`RemoveHandler` 已有守卫，只有 `RaiseEvent` 没有

## 触发面

脚本自己在顶层声明事件，紧接着在顶层 raise 它：

```vbx
Event Changed As System.EventHandler
Dim hits = 0
AddHandler Changed, Sub(sender As Object, e As System.EventArgs) hits += 1
RaiseEvent Changed(Nothing, System.EventArgs.Empty)
Console.WriteLine(hits)
```

## 症状与实测

顶层 `RaiseEvent Changed(...)` 报 **`BC30188`**（`ERR_ExpectedDeclaration`，「应为声明」）**+ `BC30205`**，语法树形态
`RaiseEventKeyword<RaiseEventAccessorStatement<CompilationUnit` —— 关键字被读成 **Custom Event 的 `RaiseEvent` 访问器声明**，
`members` 里是 `EventStatement, RaiseEventAccessorStatement`。`.vbx` 与 REPL 两条路径同码。

判别性对照（同一编译器，只换容器）：

| 容器 | 实测 | 形态 |
|---|---|---|
| 顶层语句 | `BC30188`+`BC30205` | `RaiseEventAccessorStatement` |
| 顶层 `Sub` 体内 | `CLEAN` | `RaiseEventStatement` |
| 嵌套 `Class` 的方法体内 | `CLEAN` | `RaiseEventStatement` |
| `Custom Event` 块内 | `CLEAN`（访问器） | `RaiseEventAccessorStatement` |

## 根因

`Parser.vb` 的声明位分派里，`RaiseEventKeyword` 这一支**没有 `IsTopLevelScript` 守卫**：

- `Parser.vb:885-897`：`AddHandler` / `RemoveHandler` 都有守卫 —— 顶层走 `ParseStatementInMethodBodyInternal()`，否则才走访问器分支。
- `Parser.vb:899-905`：`RaiseEvent` 无条件走 `ParsePropertyOrEventAccessor(SyntaxKind.RaiseEventAccessorStatement, …)`。

同族三支分叉没有设计依据：`spec\spec-scripting-dialect.md:189-198` 把 `AddHandler`/`RemoveHandler` 明文定为「顶层按语句解析」，
`RaiseEvent` 却被写成「顶层报 unsupported 诊断」（原 `spec:438` 的 Testing 清单一句），而设计表（`:283-295`）没有这一条。
语义上也站不住：脚本类**自己声明**了该事件（顶层 `Event` 是脚本类成员），`RaiseEvent` 只能 raise 本类事件，这个位置本来就是它的合法落点；
raise 的崩溃族（issue 13 / 20）修掉之后，顶层 raise 已经能正确降级。

## 预期行为

顶层 `RaiseEvent` 是**普通语句**：解析成 `RaiseEventStatement`，作为实例初始化器的一个条目执行，挂接的 handler 真的被调用。
`Custom Event` 块内的同名关键字仍是访问器声明（该块内 `IsTopLevelScript` 为假）。宿主对象或上一提交声明的事件仍不可 raise ——
那是 `RaiseEvent` 只能指向本类事件的既有语言规则，绑定期报 `BC31132`。

## 修复方向（已落地）

`Parser.vb:899-905` 补上与 `AddHandler`/`RemoveHandler` 同形的守卫：

```vb
Case SyntaxKind.RaiseEventKeyword
    If IsTopLevelScript Then
        Return ParseStatementInMethodBodyInternal()
    End If
    Return ParsePropertyOrEventAccessor(SyntaxKind.RaiseEventAccessorStatement, Nothing, Nothing)
```

配套文档与测试：

- `spec\spec-scripting-dialect.md`：§「Top-level `AddHandler`, `RemoveHandler` and `RaiseEvent`」写成同族第三支，并说明顶层 raise 的是脚本类自己声明的事件、
  作用范围是初始化器体；Testing 清单里「顶层 `RaiseEvent` 报 unsupported 诊断」改为「运行已挂接的处理器」。
- `ScriptModeStatementConformanceTests`：`TopLevelRaiseEventStatement_IsReported` → `_Conforms`，断言 `AddHandler` 之后 `RaiseEvent` 让计数为 `1`，
  并保留原 `"OK"` 形态。
- `ScriptTests`：`TestTopLevelRaiseEventStatementReportsUnsupportedDiagnostic` → `TestTopLevelRaiseEventStatement`：脚本自声明事件 raise 后计数 `1`；
  宿主对象的事件改断言绑定期 `BC31132`（不再是解析期误读）。
- `SubmissionSharedHandlesHookupTests`：只改过时注释（原注释称顶层 `RaiseEvent` 不是合法语句）。

## 修复后的新增行为变化

| 形状 | 修复前 | 修复后 |
|---|---|---|
| 顶层 `Event` + `AddHandler` + `RaiseEvent` | `BC30188`+`BC30205`（访问器声明） | `CLEAN`，形态 `RaiseEventStatement`，handler 投递（`.vbx` 与 REPL 实测计数 `1`） |
| 顶层 raise 宿主对象的事件 | `BC30188`+`BC30205`（解析期） | `BC31132`（绑定期，`RaiseEvent` 只能 raise 本类事件） |
| `Custom Event` 块内访问器 | `CLEAN` | `CLEAN`（守卫不进入该块） |
| 未声明事件 | — | `BC30451` |

## 验证（已运行）

- 全量 battery 逐格比对：本轮 **8 格**变化，全部落在 `RaiseEventKeyword`，无崩溃、无其他关键字受影响。
- `Scripting\VisualBasicTest` 全量 **814** 格：失败 3 格，均为基线既存的环境相关用例（见 issue 40 的同一份读数）。
- 发布版宿主端到端：`.vbx` 与 REPL 的顶层 raise 脚本均 `exit 0` 且 handler 被调用。

## 相关

- 崩溃族（均为 `Fixed`，本条的语句化把顶层 raise 接到它们修好的降级路径上）：`issue-top-level-raise-event-instance-event-crash.md`（13）、
  `issue-submission-custom-event-raise-assert.md`（20）。
- 同族分派缺陷：`issue-script-mode-end-statement-rejected.md`（39）、`issue-top-level-keyword-dispatch-differs-from-method-body.md`（40）。
- **上游遗留**：`RaiseEvent` 在 async/iterator 方法体内会让 Debug 构建撞状态机断言 —— `issue-raise-event-in-resumable-method-debug-assert.md`（42，上游来源，本 fork 不修）。
- 审计与台账：`tasks\keyword-toplevel-audit\README.md`（§4 D3、§7）、`ledger.md`。
