# 脚本提交类的共享构造器（`.cctor`）不由提交构造或顶层语句触发

* 状态：**Open**（未修复，理由见文末「为什么不修」）
* 发现日期：2026-09-14
* 发现场景：收尾 `issue-top-level-handles-clause-crash.md`（14）的边界时观察到「同一次提交里 `Shared Event` + 顶层 `Shared Sub … Handles` 编译通过但 handler 不投递」，随后由独立查证线（`tmp\vortex-logs\script-top-level-crashes-2\11-investigate-u10-shared-handles.md`）定判

## 触发面与症状

顶层 `Shared WithEvents` 变量上的 `Handles` 子句——**挂钩体本身是正确注入、也能正确执行的**（hookup 由该变量的属性 setter 承担，`SourceMemberMethodSymbol.vb:792-793`），但**挂着共享字段初始化器的那个共享构造器不跑**，setter 因此不被调用、handler 收不到事件：

```vbx
Class Hook
    Shared Event E As EventHandler
    Shared Sub Fire()
        RaiseEvent E(Nothing, EventArgs.Empty)
    End Sub
End Class
Shared WithEvents hooked As New Hook
Shared Sub OnIt(s As Object, a As EventArgs) Handles hooked.E
    Console.WriteLine("H")
End Sub
hooked.Fire()
```

实测：exit `0`、**stdout 为空**、handler 未被调用（`tmp\probes\u6bc\handles-shared.vbx`，**实锤**）。零编译诊断。

**缺口的确切范围**：提交类的共享构造器**不由提交构造触发，也不由顶层语句本身触发**；它**只在共享字段被读或写时触发**（见下节表的第二行）。本形状在 `hooked.Fire()` 之前从不读写 `hooked` 这个共享字段 ⇒ `.cctor` 从不运行；只要在 raise 之前读一次该字段（`tmp\probes\u10\v1-touch-field-first.vbx`）或显式强制类型初始化器（`w4`），handler 立刻投递。⇒ **不是「共享构造器永远不跑」**，而是「触发它的只有共享字段的读 / 写」。

**挂钩体无恙的证据（实锤）**：`tmp\probes\u10\w4-touch-self-type-via-reflection.vbx` 里 `RuntimeHelpers.RunClassConstructor(t.TypeHandle)` 之后 handler 立刻投递；任意一次**共享字段读**（`v1-touch-field-first.vbx`、`v2-probe-touched.vbx`）同样会让侧写与 `H` 一起出现。

## 判据：脚本特有（**实锤**）

同一个形状放进普通编译上下文（非入口类型 `Holder` 持共享 `WithEvents`，其共享方法 `Go()` 里 raise，由 `Main` 调 `Holder.Go()`）→ **投递 `H`**（`tmp\probes\u10\m4-holder-go.vb`）；脚本同构（`w9-shared-go-analog-of-m4.vbx`）→ **不投递**。

两侧的类型都实测为 `BeforeFieldInit` + 有 `.cctor`（`m9-attrs.vb` / `oh-reflection.vb` / `m7-shared-reflect.vbx`）⇒ 差异不在类型标志，而在**执行侧的类型初始化触发规则**：

| 触发方式 | 普通上下文 | 脚本 |
|---|---|---|
| 调该类型的共享方法 | **触发**（`m8-static-call.vb`：`CCTOR` 先于 `S`；`m11`/`m13`/`m14` 换 newobj / 委托 / 异步嵌套调用同样触发） | **不触发**（`w3-touch-shared-method.vbx`：顶层调提交类自己的共享方法，`CCTOR` 侧写不出现；两次重跑一致） |
| 共享字段读 / 写 | 触发 | **触发**（`w6-field-write-in-shared.vbx`；`w2` 证明是字段读**当场**触发，不是启动时） |
| 入口类型自身 | 触发（`m1-entrytype-cctor.vb`：`CCTOR` 先于 `MAIN`） | **不触发**（`w1-cctor-probe-only.vbx`：只输出 `body`） |

## 附带事实：脚本里的共享字段初始化器全是惰性的（**实锤**）

因为提交类的 `.cctor` 只被共享**字段**的读 / 写触发，**所有**共享字段初始化器都推迟到第一次访问该字段才执行。

**既有验收探针是一处真空控制**：`tmp\probes\u6bc\shared-init-runs.vbx` 自己在顶层读了那个字段，所以输出 `SHARED-INIT-RAN` / `probe=7` 看起来证明「共享初始化器确实执行」；把那次读去掉（`w1`）就完全不执行。⇒ 该探针**不能**作为「共享初始化器在启动时执行」的证据。

## 根因候选（已检查）

`Emit\NamedTypeSymbolAdapter.vb:466-499`：隐式声明的共享构造器默认打 `BeforeFieldInit`（`:475-478` 对**显式**声明的 cctor 返回 `False`；`:496-499` 是默认放行），只有 `:482-494` 的扫描命中 `handledEvent.hookupMethod.MethodKind = MethodKind.SharedConstructor`（`:488`）才抑制该标志。

共享 `WithEvents` 形状的 hookup 宿主是**属性 setter**（`Symbols\Source\SourceMemberMethodSymbol.vb:792-793` 的 `handlesKind = HandledEventKind.WithEvents → hookupMethod = witheventsPropertyInCurrentClass.SetMethod`），不是共享构造器 ⇒ 该抑制不生效 ⇒ 提交类照打 `BeforeFieldInit`（`m7` 反射实测吻合）。即：VB 只对「`Handles` 接共享构造器」这一种形状放弃 `beforefieldinit`，共享 `WithEvents` 形状本来就不在其列。

## 未闭合（**推测**）

**为什么普通类型的方法调用会触发其 `.cctor`、而提交类的方法调用不会**——两侧都实测 `BeforeFieldInit` + 有 `.cctor`，行为却不同。查证线在「不新建编译工程」的约束下没有 IL/CLR 层的反汇编证据，故只到候选层。下一步可复核的做法：对普通 `m8-static-call.exe` 与提交程序集分别 dump 入口 / `<Factory>` / 共享方法的 IL 与 TypeDef 标志位对比。

## 预期行为

与「同样一份普通程序」一致：执行提交时触发提交类的类型初始化器，共享字段初始化器与依赖它们的 `Handles` 挂钩在顶层语句之前生效。（或：明确判为「脚本不支持共享初始化器依赖 cctor 时机」并给出诊断——两条路都要作者裁决。）

## 修复方向

1. **宿主侧**：执行提交前触发其类型初始化器（或在类型初始化标志上对提交类放弃 `BeforeFieldInit`），使共享初始化器的执行时机与普通程序一致。
2. **发射侧**：把 `NamedTypeSymbolAdapter.vb:482-494` 的抑制条件从「`hookupMethod Is SharedConstructor`」放宽到「hookup 最终由共享字段初始化驱动」。

方向 1 改动面小但影响**所有**脚本共享初始化器的运行时机；方向 2 只动标志判定，但仍要解释上述未闭合的触发差异。

## 为什么不修

1. **它不是崩溃**：exit `0`、零诊断，没有坏 IL，也不终止进程——它是「本该正常却悄悄不跑」。
2. **修法会改变所有脚本共享初始化器的运行时机**：已验收的共享初始化器修复（`SourceMemberContainerTypeSymbol.vb:2747-2752`）就建立在这套时机上，改时机等于改语义，需要作者裁决。
3. **机制未闭合**：触发规则差异的 CLR 层原因仍是**推测**，此时动手等于猜。

## 修复后的新增行为变化

修复后**不新增诊断**，但会改变运行时机：今天「惰性到首次共享字段访问」才执行的共享字段初始化器（以及挂在其上的 `Handles` 挂钩）会提前到提交执行前 ⇒ 依赖该时机的脚本行为会变（含 `Shared WithEvents` + `Handles` 的 handler 从「不投递」变为「投递」）。须补回归用例：

- 共享 `WithEvents` + `Handles` 的投递计数（脚本与普通对照各一）；
- 「共享字段初始化器在顶层语句之前执行」的时序用例（**不得**用「在顶层读该字段」的写法构造，那是真空控制）；
- 惰性触发本身若被保留，则须有一条用例锁住「不读共享字段时不执行」。

## 相关

- `issue-top-level-handles-clause-crash.md`（14）：同提交内的 `Handles` 判据与跨提交 / 宿主对象的 `BC37343` 边界；本 issue 是该边界之外的第三种形状（同提交 + 共享事件）。
- 第一轮已验收的共享初始化器修复（提交类共享初始化器改注入参数化共享构造器）：`SourceMemberContainerTypeSymbol.vb:2747-2752`；**它不是本 issue 的回归**——强制类型初始化器后该修复产物工作正常（`v2` / `w4`）。
- 脚本执行路径：顶层语句住在实例方法 `<Initialize>` 里、宿主入口是静态 `<Factory>`（`Scripting\Core\ScriptExecutionState.cs:79-83`；`.vbx` 走脚本路径而非编译路径，`Interactive\vbi\Vbi.vb:58-62`）。
