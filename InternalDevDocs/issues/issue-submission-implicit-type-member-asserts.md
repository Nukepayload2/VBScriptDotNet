# [BUG] 提交类顶层 `Event` / `WithEvents` 成员 → Debug 断言终止（提交类 `IsImplicitlyDeclared` 为 True）

**状态**：Open（2026-09-12 登记）
**证据等级**：**已运行**（Debug `2.0.0-Beta+5816a5c`，本机实测，含嵌套类容器对照）
**严重度**：高（用户可达、编译期零诊断、Debug 直接终止进程）
**影响面**：任何 `TypeKind.Submission` 编译（`.vbx` 脚本执行、vbi REPL 提交）；`vbc` 不产生提交编译，不受影响

## 症状

提交类顶层写 `Event` 或 `WithEvents` 成员，Debug 构建终止进程（`EXITCODE=35`）：

```vb
' probe A
Event E As EventHandler
Console.WriteLine("EVENT-OK")
```

```text
Process terminated.
Assertion failed.
Not ContainingType.IsImplicitlyDeclared
   at …Symbols.Source.SynthesizedEventAccessorSymbol.AddSynthesizedAttributes(…) …SynthesizedEventAccessorSymbol.vb:line 495
   at Microsoft.CodeAnalysis.VisualBasic.Symbol.GetCustomAttributesToEmit(…) …Emit\SymbolAdapter.vb:line 84
```

```vb
' probe B
Class Raiser
    Event SomethingHappened As EventHandler
End Class
WithEvents r As New Raiser
```

```text
Process terminated.
Assertion failed.
Not Me.ContainingType.IsImplicitlyDeclared
   at …Symbols.Source.SourceWithEventsBackingFieldSymbol.AddSynthesizedAttributes(…) …SourceWithEventsBackingFieldSymbol.vb:line 66
   at Microsoft.CodeAnalysis.VisualBasic.Symbol.GetCustomAttributesToEmit(…) …Emit\SymbolAdapter.vb:line 84
```

**编译期零诊断**；崩溃发生在发射期合成特性的过程中（`AddSynthesizedAttributes`）。

## 实测边界（已运行，2026-09-12，Debug `vbi.exe` 直跑 `.vbx`）

| 顶层写法 | 结果 |
|---|---|
| `Event E As EventHandler` | ❌ 断言终止（`SynthesizedEventAccessorSymbol.vb:66`→`:495`） |
| `Shared Event E As EventHandler` | ❌ 同上 |
| `WithEvents r As New Raiser`（实例） | ❌ 断言终止（`SourceWithEventsBackingFieldSymbol.vb:66`） |
| `Shared WithEvents r As New Raiser` | ❌ 同上 |
| 嵌套类里的 `Public Event E As EventHandler` + 建实例 | ✅ 正常（`NESTED-EVENT-OK`，exit 0） |
| 嵌套类里的 `Public WithEvents r As New Raiser` + 建实例 | ✅ 正常（`NESTED-WITHEVENTS-OK`，exit 0） |

**触发条件 = 成员落在提交类本身，与 `Shared` 无关**（上表第 1 行 vs 第 2 行、第 3 行 vs 第 4 行逐对隔离：实例形状同样终止），**与「是否为 `WithEvents`」也无关**（`Event` 与 `WithEvents` 是两个不同的断言点）。

## 根因（源码核实）

**提交类的符号类型是 `ImplicitNamedTypeSymbol`，而它把 `IsImplicitlyDeclared` 报成 True。**

1. `Compilers\VisualBasic\Portable\Symbols\Source\SourceMemberContainerTypeSymbol.vb:233-237`——`DeclarationKind.Submission`（连同 `ImplicitClass`、`Script`）走 `New ImplicitNamedTypeSymbol(...)`，而其它种类走 `New SourceNamedTypeSymbol(...)`（`:239-240`）。**证据等级：已检查。**
2. `Compilers\VisualBasic\Portable\Symbols\Source\ImplicitNamedTypeSymbol.vb:33-37`——`IsImplicitlyDeclared` 返回 `IsImplicitClass OrElse IsScriptClass`，提交类 `IsScriptClass` 为真（`SourceMemberContainerTypeSymbol.vb:1295-1300`：`DeclarationKind.Script` 或 `Submission`），故 **True**。（对照：`SourceMemberContainerTypeSymbol.vb:1289-1293` 的基类实现恒返回 `False`，嵌套类因此不受影响。）**证据等级：已检查。**
3. 提交类的顶层成员由声明表整棵塞进脚本类：`Declarations\DeclarationTreeBuilder.vb:175-198`——`SourceCodeKind` 非 `Regular` 时，编译单元里**除 namespace 外**的每个成员都进 `scriptChildren`（`:184-188`），随后 `CreateScriptClass`（`:196`）。因此顶层 `Event` / `WithEvents` 声明会以成员身份落在 `ImplicitNamedTypeSymbol` 上。**证据等级：已检查（源码）+ 已运行（探针 A/B 与之相符）。**
4. 两处合成成员各自断言「容器不是隐式声明的类型」：
   - `Symbols\Source\SynthesizedEventAccessorSymbol.vb:495`（事件 add/remove 访问器）`Debug.Assert(Not ContainingType.IsImplicitlyDeclared)`；
   - `Symbols\Source\SourceWithEventsBackingFieldSymbol.vb:66`（`WithEvents` 后备字段，构造点 `Symbols\Source\SourcePropertySymbol.vb:272`）`Debug.Assert(Not Me.ContainingType.IsImplicitlyDeclared)`。

   **同族第三处**（本 issue 未单独实测）：`Symbols\Source\SynthesizedWithEventsAccessorSymbol.vb:93` 同一断言式。三处是本文件可枚举的全部 —— 判据 `grep -rn "IsImplicitlyDeclared" Compilers\VisualBasic\Portable --include=*.vb | grep -i assert` 命中 4 条，其中 3 条是 `Not …IsImplicitlyDeclared`（第 4 条 `MethodCompiler.vb:1825` 是另一话题）。**证据等级：已检查。**

**Release 下的表现未复现**：`Debug.Assert` 在 Release 编译掉，之后是否继续发射、是否产生别的症状，未测（见「未复现 / 未查」）。

## 预期行为

顶层 `Event` / `WithEvents` 要么正常工作（成员落在提交类上并正确发射特性），要么给出编译诊断。现状是**两者皆无**——编译通过、Debug 崩进程。

## 修复方向（候选，未拍板）

- **A. 放宽两处（三处）断言**：把 `Debug.Assert(Not ContainingType.IsImplicitlyDeclared)` 改为「隐式声明的容器不追加合成特性」或直接去掉该前提。理由：断言想表达的可能是「这些特性只对用户可见类型有意义」，而提交类恰恰是用户代码的落脚点。代价最小，但需先查清该断言原始意图（上游 `dotnet/roslyn` 历史）。
- **B. 让提交类不报 `IsImplicitlyDeclared`**：把 `ImplicitNamedTypeSymbol.vb:33-37` 的判据从「`IsImplicitClass OrElse IsScriptClass`」收窄到「`IsImplicitClass`」，或给 `DeclarationKind.Submission`/`Script` 单独分支。改动面极小，但 `IsImplicitlyDeclared` 的消费者众（例如 `Symbol.GetCustomAttributesToEmit`、`CSharp` 侧同名判据），**影响面未清点**（未做）。
- **C. 给顶层 `Event` / `WithEvents` 报诊断**：`Event` 不在 `spec\spec-scripting-dialect.md:11-18` 的顶层四形式映射表内（见该 spec 的 `:56`、`:273` 两处「穷尽」主张），若判定它不是脚本受支持形状，则应在绑定期报诊断而不是崩。与 A/B 不互斥。

三者共同的前置：**先查清三处断言的原始意图**（为何假定容器非隐式声明），再决定是放宽断言还是纠正 `IsImplicitlyDeclared`——否则 A 与 B 只是把断言搬走。

## 关联

- `InternalDevDocs\proposals\proposal-submission-shared-members.md`——本 issue 由该提案的「同一族还有别的缺口」清点发现（探针 A/B 原为「`Shared` 事件 / `Shared WithEvents` 字段」两问，实测发现与 `Shared` 无关）。
- `InternalDevDocs\proposals\proposal-with-events-in-submissions.md`——提交里的 `WithEvents` / `Handles` 语义提案。该提案的实测面是**绑定期**崩溃（`SourceMemberMethodSymbol.BindSingleHandlesClause` 落 `UnexpectedValue`）与 `Handles` 挂/摘钩语义；本 issue 是**发射期**的断言族，两者触发点不同，不可互相覆盖。
- `InternalDevDocs\issues\issue-script-top-level-extension-method-crash.md`（issue 04）——同属「提交类顶层成员遇到只为普通类写的前提」形状。
- `InternalDevDocs\spec\spec-scripting-dialect.md:348`——「`WithEvents` in a submission class … A `Handles` clause in a submission class is not supported: no diagnostic is reported for it, and the compilation does not complete.」本 issue 是同一片边界的另一个终止点（无 `Handles` 子句也终止）。
