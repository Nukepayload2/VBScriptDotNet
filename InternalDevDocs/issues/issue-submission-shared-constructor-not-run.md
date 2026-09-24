# 脚本提交类的共享构造器（`.cctor`）不由提交构造或顶层语句触发

* 状态：**Open → 拆分裁定**（2026-09-22 按 `../decisions.md` **D7** 取证后拆成 A/B 两半：A 判**不修**（与 C# 同形的 CLR 语义），B 判**必修**（本 fork 自开的脚本层缺口，落点属 D7 例外 (c) 需人工）。详见下节「D7 裁定」。原「为什么不修」三条已被取证反驳，见该节末。）
* 发现日期：2026-09-14
* 发现场景：收尾 `issue-top-level-handles-clause-crash.md`（14）的边界时观察到「同一次提交里 `Shared Event` + 顶层 `Shared Sub … Handles` 编译通过但 handler 不投递」，随后由独立查证线（`tmp\vortex-logs\script-top-level-crashes-2\11-investigate-u10-shared-handles.md`）定判

## D7 裁定（2026-09-22，只读取证批次；日志 `tmp\vortex-logs\csharp-script-parity-sweep\01-dig-sweep-26-18.md`）

本 issue 把**两件事**写在了一条标题下，两半的裁定**相反**，必须拆开：

### A · 共享字段初始化器惰性 → **判「不改」**（与 C# 同形的 CLR 语义）

* 两侧 `.cctor` 的**合成门槛**一致：C# `SourceMemberContainerSymbol.cs:5714-5718`（`!hasStaticConstructor && hasNonConstantInitializer(StaticInitializers)`）↔ VB `SourceMemberContainerTypeSymbol.vb:2747-2752`。
* 两侧 **beforefieldinit 口径**一致：C# `Emitter\Model\NamedTypeSymbolAdapter.cs:515-538`（隐式 `.cctor` ⇒ 置 beforefieldinit = true，即 C# 明确**不承诺**静态字段初始化器的执行时机）↔ VB `Emit\NamedTypeSymbolAdapter.vb:475-478`/`:496-499`。
* 实测一致（发布版宿主，改动前面貌）：`Shared b As Integer = 42` ⇒ `?b` 得 `42`（`.cctor` 存在且首次访问触发）；`Shared a As Integer = F()`（`F` 体内打印）⇒ 脚本体执行期间**不打印**。
* ⇒ **义务只剩文档与测试**：`spec` 写清「脚本里 `Shared` 初始化不保证在脚本体之前跑，首次触碰才跑」，并补**正向对照**（读了 ⇒ 断到值；不读 ⇒ 无副作用）。本 issue 现有的「真空控制」批评（`shared-init-runs.vbx` 自己读了那个字段）继续有效，那正是这条口径必须被钉住的原因。

### B · `Shared Sub … Handles` 挂钩静默丢失 → **判「必修」**，落点属 D7 例外 (c) 上报人工

* 机制已定位（**实锤，读码 + blame 归属**）：本 fork 在 `7edb77de9`（2026-09-15「fix crashes」）把 `Handles` 的合法容器扩到提交类（`SourceMemberMethodSymbol.vb:778-780`，逐字注释 *"A submission class is a class container and the hookup host … is an instance or shared constructor, which it has as well."*），共享挂钩宿主是**硬取** `ContainingType.SharedConstructors(0)`（`:795-797`）；但上游的 `AddWithEventsHookupConstructorsIfNeeded` 对提交类**整段跳过**——`SourceMemberContainerTypeSymbol.vb:2829-2832` 第一行逐字 `If TypeKind = TypeKind.Submission Then 'TODO: anything to do here?`（`git blame` 归 `e814cb1 "add base compiler"`）。⇒ **只有 shared `Handles`、没有 shared 字段初始化器时，提交类根本没有可注入挂钩的构造器**，挂钩静默消失。
* 与 A 的关键区别：这不是 CLR 时机问题，而是**本 fork 开了口没接线**。VB 自己就要求「`Handles` 落在 `.cctor` 时必须按时执行」——`NamedTypeSymbolAdapter.vb:482-492` 正是为此**抑制** beforefieldinit；A 的惰性口径不构成 B 的免责理由。
* **停手 ≠ 不修**：D7 三问在 B 上答 ②否 ③否（C# 无 `Handles`/`WithEvents`/共享事件挂钩概念 ⇒ 判不出挂钩该落 `.cctor` 还是脚本初始化器；两个以上等价形态 ⇒ 命中例外 (c)），故**落点选择交人工裁定**；但「静默丢弃用户写下的挂钩」在任何一侧语义里都不成立，**不依赖 C# 判据**即成立为缺陷。
* **建议落点（供人工裁定，非自动裁定）**：把 `TypeKind.Submission` 纳入 `SourceMemberContainerTypeSymbol.vb:2829-2912` 的挂钩-构造器合成（有 shared 挂钩 ⇒ `EnsureCtor(isShared:=True)`，复用 `48d8edbff` 已修好的**无参**提交类 `.cctor` 路径），并确认 `NamedTypeSymbolAdapter.vb:482-492` 的抑制在提交类上生效。次选（把挂钩落进脚本初始化器 `Sub Main`/`<Initialize>`）风险更大：每次 `ContinueWith` 新建实例会**重复挂钩**，且把类型级语义改成实例级。
* **取证未达的一面（须由实施者先量）**：`Shared Event` + `Shared Sub … Handles` 形状在本轮 4 条 REPL 探针里**未复现**（逐行提交把块切开、顶层 `Event` 形状报 BC30287/BC30188/BC30205）⇒「静默不投递」沿用本 issue 原有的 `.vbx` 实锤，而 `SharedConstructors(0)` 裸索引在没建成时究竟是**静默丢弃还是 ICE** 属**推测**，实施第一步就该用单测把它钉死。

* **实施期真值更正（2026-09-23，main 派工的"真值先行"格实测）**：本半边的症状**不是**「exit 0、零诊断、handler 不投递」——那是 `Shared WithEvents` 形状（§触发面与症状）。真正由 `Shared Sub … Handles Me.Ev`（无共享字段初始化器）触发的是 **ICE**：`SharedConstructors` 计数为 0，`GetDiagnostics()` 与 `Emit()` 双双抛 `IndexOutOfRangeException`，栈顶逐字 `SourceMemberMethodSymbol.vb:797`（就是那句裸索引 `SharedConstructors(0)`）⇒ 比"静默丢弃"更严重，属「崩编译器」强形态。两个相邻形状同时量清：裸 `Handles Ev` 报 **BC30287**、`Handles Hook.Ev`（类型名容器）报 **BC30506** ⇒ 提交类里合法容器只有 `Me.` / `MyClass.`。
* **修复已接线（同批）**：`SourceMemberContainerTypeSymbol.vb:2829-2904` 的提交类分支（原 `'TODO: anything to do here?`）改为收集 `IsShared` 且带 `Handles` 的处理器、只认关键字容器、**且事件也为 shared** 时才 `EnsureCtor(isShared:=True)`；`Class`/`Module` 分支一字未动。T2–T7、R1–R3 全部实锤（投递计数 1/2、链上 1、两次运行 1→2 不重复挂钩、与共享初始化器共存 1015、惰性仍为 True、`..cctor` 体内无 `Me`/`ldarg`）；仅 PE 里 `beforefieldinit` 标志的直读未做（该测试工程解析不到 `PEReader`，改走行为断言）。
* **仍开放**：**来自早先提交的共享事件**走 `Handles` 仍 ICE（探针 `tmp\probe-sweep\probe-chain.txt`）⇒ 那是跨提交可见性与挂钩宿主的另一条线，另立登记，不由本条顺手修。

### 对 issue 原有内容的三处更正

1. **正文「触发面与症状」用的是 `Shared WithEvents` 形状**，其 hookup 宿主是属性 **setter**（`SourceMemberMethodSymbol.vb:792-793`）；而 B 说的是 `Shared Sub … Handles`（宿主 = 共享构造器）。两者机制不同，原正文与新增 B 半不能混读——判据第 51-53 行已把这一点写对，本节只是把两半的**裁定**分开。
2. **「为什么不修」三条已被取证反驳**：①「不崩」不再成立为理由（D6：beta 期不得用兼容性/稳定性口径压缺陷）；②「改时机等于改语义需作者裁决」——**时机不用改**，B 只要求 `.cctor` **存在**，A 的惰性口径原样保留；③「机制未闭合」——B 半边已闭合（上面四处文件:行 + blame 归属），仍开放的只剩「普通类型方法调用触发 `.cctor`、提交类方法调用不触发」的 CLR 层原因（属 A 的解释线，不阻塞 B）。
3. 附带订正：原 `SourceMemberContainerTypeSymbol.vb:2747-2752` 的注记说那次修复是「改注入**参数化**共享构造器」——`48d8edbff` 实际改成注入**无参** `.cctor`（逐字注释 *"A shared constructor cannot take the submission array parameter…"*），当时记录的「共享构造器带实例版形参」缺陷**已在当前树里修掉**。

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
