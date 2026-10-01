# issue 36：提交类共享 `Handles` 挂钩挂在 `.cctor` 上，但"初始化时机"这件事没有任何实测保证

- **登记日期**：2026-09-24（main）
- **状态**：**Fixed**（已验证，commit 待作者提交后补）——**走甲**（保持 `.cctor` 承载挂钩、把保证显式化），**产品代码零改动**。四格实测全部完成且结论一致：**`beforefieldinit` 位确实被清除**（提交类 `attrs=0x00000101`；两个对照——只有共享初始化器的提交类 `0x00100101`、普通 `Class`/`Module`——**位都还在** ⇒ 普通 VB 语义未被连带改坏）；"只调一次共享方法"时挂钩已就位；幂等成立；异步侧测过的形状**未出现丢失**。**没有任何一条实测指向乙**（改成显式幂等异步初始化门）。计划 `..\tasks\submission-shared-async-init\`、流水账 `..\..\tmp\vortex-logs\submission-shared-async-init\`、规范 `..\spec\spec-scripting-dialect.md` 新增 `### Shared Handles in a submission`（中英两份）。
- **交付的五条回归钉**（`Compilers\VisualBasicEmitTest\Emit\SubmissionSharedHandlesHookupTests.vb`，六格）：**属性位正反同格**（有钩无位 / 只有初始化器有位，并断"只差这一位"＋两侧都确有 `.cctor`，堵住"无 `.cctor` 时位自然不在"的空洞格）；**鉴别投递**（只调共享方法、计数在无关类型上，且**只删 `Handles` 两行**的变体证明探针非空洞）；**`.cctor` 早于提交体第一句**；**幂等断投递次数**（两次 raise 投递两次而构造器跑一次；跨提交两处理器各一次）；**普通 VB 对照**（`Class`/`Module` 含位、显式 `Sub New` 不含位）。四条变异各自命中**目标**用例——其中把抑制**放宽**（`Return True`→`Return False`）那种正是单侧断言抓不到的方向，由属性位钉的反例一侧与投递钉的反例变体抓住。
- **收口时的插曲（新登记，非本条缺陷）**：新增格落在 **Emit 门**，使 `scripts\verify-vb-compiler-tests.ps1` 写死的基线数字失配并 `throw`，**把第七门 CommandLine 整个跳过**（`GATES_EXIT=1` 而失败数为 0）；基线已同步（4382→4388、4279→4285），该脚本亦已改为**失败时保留并打印 trx 路径**（偶发红的 payload 原先会被 `finally` 删掉）。另该新类在全量 Emit 门下的偶发红**已定性为 issue 35 的第二个受害者**（payload 逐字同签名；并经三条变异——删掉被测的 `Handles` 处理程序、去掉跨程序集 `Take()`、ALC 改可回收——证明**不是本批引入**，只有"去掉邻居类并发"才让红消失）——**归 issue 35，本条不背**。
- **来历**：作者复核 issue 18-B / 31 的修法时提出：Release 下 VB **模块**的 `Sub New` 在不访问字段时不执行；如果**类**发生同样行为就难办。应当有一个"脚本共享异步初始化"的位置，并保持幂等。
- **与 issue 18-B 的关系**：不推翻它。18-B 修的是"根本没有宿主可注入"（越界崩溃），这条说的是"有了宿主之后，正确性押在一条没人验证的元数据位上，而且这个宿主装不了异步"。

## 一、已核实的代码事实

1. **发射侧的决定性分支**：`Compilers\VisualBasic\Portable\Emit\NamedTypeSymbolAdapter.vb:475-499`
   - 有**显式** `Sub New`（`Not cctor.IsImplicitlyDeclared`）⇒ `Return False`（不写 `beforefieldinit`）；
   - 隐式 `.cctor` 且某方法的 `HandledEvents` 中存在 hookup 宿主为 `MethodKind.SharedConstructor` ⇒ `Return False`；
   - 否则 ⇒ `Return True`，注释逐字："唯一目的就是初始化若干字段，因此可以推迟到访问字段时"。⇒ **这就是作者说的模块 `Sub New` 不执行那条路**。
   ⇒ 提交类走不走坑，取决于第二条有没有命中；命中则 CLR 精确初始化语义保证"任何静态成员访问（含静态方法调用）之前先跑 `.cctor`"。
2. **仓内已有读这个位的能力**（所以本任务的取证不需要造新工具）：`Compilers\VisualBasicEmitTest\Emit\EmitMetadata.vb:605` `EmitBeforeFieldInit` + `:646/:678` 断 `row.Attributes`；`Compilers\VisualBasicSemanticTest\Semantics\FieldInitializerBindingTests.vb:222` 用 `Assert.False(IsBeforeFieldInit(typeSymbol))`。
3. **既有验收格不断这个位**：`Compilers\VisualBasicEmitTest\Emit\SubmissionSharedHandlesHookupTests.vb` 断的是"合成出无参共享构造器""`HandledEvents(0).hookupMethod.MethodKind = SharedConstructor`""IL 里 `AddHandler` 条数"——**没有一格断发射出来的 `TypeAttributes`**。队列 #9 采纳方案时写明要"确认 `NamedTypeSymbolAdapter.vb:482-492` 的 beforefieldinit 抑制生效"，这半句未落实。

## 二、"投递成功"为什么不等于"时机正确"（作者这一问的要害）

现有观察投递的探针形状（`SubmissionSharedHandlesHookupTests.vb:52-56`）：

```
Shared Sub Fire()
    RaiseEvent Ev(Nothing, System.EventArgs.Empty)
End Sub
Fire()
System.Console.Write(Sink.Count())
```

三条理由使它**不能鉴别**时机：

1. `Fire()` 与 `Sink.Count()` 都是**共享方法调用**。CLR 保证"静态字段访问触发初始化"；只有**非** `beforefieldinit`（精确语义）才保证"静态方法调用也触发"。所以调用形式本身不构成对精确语义的检验。
2. 被读的共享字段在**另一个类型 `Sink`** 上 ⇒ 只会触发 `Sink` 的初始化，不会触发提交类的。
3. 即便 `beforefieldinit` **没有**被抑制，CLR 也**允许**提前执行 `.cctor` ⇒ "看到了投递"在两种情况下都成立。

⇒ 结论：这一格绿，**推不出**"挂钩在 `RaiseEvent` 之前已就绪"。要判必须另做两格（见 §四）。

## 三、`.cctor` 承载共享初始化的两个结构性限制

1. **不能异步**：脚本的 `<Initialize>` 是异步状态机，而 CLR 类初始化器不能 `await`。事件源若在异步路径里构造（例：`Shared` 的事件持有者来自一次 `Await`），`.cctor` 没有"等它就绪"的位置。
2. **没有显式幂等门**：交互链上每个 `Submission#N` 各自一个类、各自一个 `.cctor`；同一事件的多次挂钩、跨提交重复挂钩，目前是靠 CLR"每个类型只初始化一次"顺带保证的，仓内没有一条断言写"恰好一次"。作者要求的"幂等"是显式契约，不是副作用。

## 四、要跑的两格实测（决定后面走哪条路）

- **T1（读元数据）**：发射一个只含 `Shared Event` + `Shared Sub H(...) Handles Me.E` 的提交，断提交类的 `TypeAttributes` **不含** `BeforeFieldInit`；同时以"没有共享挂钩的提交类"和"只有共享字段初始化器的提交类"作对照（后两者**应当含**该位，否则普通 VB 语义会被连带改掉）。可复用 §一.2 的现成手段。
- **T2（真正鉴别的投递实验）**：让提交体内**除了一次共享方法调用以外不触碰任何静态成员**（计数容器换成与提交类无关的独立类型，或者干脆把计数写进 `H` 自己的实例里、由外部一次性读取），再判断"handler 是否在 raise 之前已挂上"。T1 与 T2 的四种组合要分别记录（有位/无位 × 投递/不投递）。
- **T3（幂等）**：同一事件在两个不同提交里各有一个处理器 ⇒ 断**恰好两个**处理器各投递一次，不多不少（现有 `TwoSharedHandlers_SingleSynthesizedSharedConstructor` 只断构造器数量，没断投递次数）。
- **T4（异步）**：事件持有者由 `Await` 构造 ⇒ 记录当前行为（挂钩丢失？时序错？还是恰好可用？），不要预设结论。

## 五、判据（结果出来后二选一，不预设）

- **甲（若 T1 显示位确实被抑制、T2 显示时机正确）**：保持 `.cctor` 承载挂钩，但**把保证显式化**——补 T1/T2/T3 三格回归钉 + 在 `spec` 写清"共享挂钩在首次触碰该提交的任何静态成员前已完成"。异步那条另立。
- **乙（若 T1 位未被抑制，或 T2/T4 显示时机不可靠）**：改为**显式的共享异步初始化入口**——一个"首次调用者启动、其余等待同一任务"的门（`Lazy(Of Task)` 式），保证恰好一次；`.cctor` 只保留 CLR 允许它做的事。这会改语义，需要新的实现任务。
- 两种情况下都必须回答"模块与类为什么不同"：因为 `Return True` 那条分支只对**只含字段初始化**的隐式 `.cctor` 生效。这条解释要落在 issue 正文里，不能只留在注释。

## 六、影响面

- 产品侧：`.vbx` 文件执行与 REPL 提交中，脚本自己写的 `Shared Event` + `Handles`；异步事件源。
- 普通编译（`vbc`）不受影响——提交类是脚本专有容器；但**发射侧那个判据是共用的**（`NamedTypeSymbolAdapter` 对所有类/模块生效），所以任何后续改动都必须带"普通类/模块的属性位不变"的对照格。

## 七、"模块与类为什么不同"——机制解释（读代码确认的）

发射侧那段判据（`Compilers\VisualBasic\Portable\Emit\NamedTypeSymbolAdapter.vb:475-499`）**对模块和类是同一段代码**，发射侧不区分二者。差别出在 VB 绑定器：**模块根本写不出"由自己的 `.cctor` 托管的 `Handles`"**，所以"有 hookup 就抑制"那条分支**对模块不可达**，模块只要有隐式 `.cctor` 就必然走 `Return True`。

三处锚点：

1. **判据的分支顺序与结构性谓词**（`NamedTypeSymbolAdapter.vb:475-499`）
   - `:475-478` 显式 `Shared Sub New`（`Not cctor.IsImplicitlyDeclared`）⇒ `Return False`；
   - `:482-494` 扫 `GetMembers()` 里每个 `MethodSymbol` 的 `HandledEvents`，只要一条的 `hookupMethod.MethodKind = MethodKind.SharedConstructor`，`:489` 就 `Return False`；
   - `:499 Return True` 排在这个循环**之后**。
   ⇒ "只含字段初始化"不是注释里的一句承诺，而是**结构上的**：`Return True` 唯一的进入路径是"隐式 `.cctor`"且"循环一条都没命中"。一旦 `.cctor` 里有 `AddHandler`，`:489` 必然命中。
2. **模块被挡在第 2 条之外**（`SourceMemberMethodSymbol.vb:618-621`）：`Me.ContainingType.IsModuleType AndAlso singleHandleClause.EventContainer.Kind <> SyntaxKind.WithEventsEventContainer` ⇒ 报 `ERR_HandlesSyntaxInModule`（BC31418）并 `Return Nothing`。模块的 `Handles` **必须**指向 `WithEvents` 变量，`Me`/`MyClass`/`MyBase` 一律被拒。
3. **`.cctor` 宿主只可能由 `Me`/`MyClass`/`MyBase` 产生**（`SourceMemberMethodSymbol.vb:792-797`）：`:792-793` `handlesKind = WithEvents` ⇒ 宿主是 `witheventsPropertyInCurrentClass.SetMethod`（属性 setter）；否则 `:795-797` 方法与事件都共享时，宿主是 `Me.ContainingType.SharedConstructors(0)`。对模块而言，`:792` 那一支是**唯一**能走到的分支。
   ⇒ 模块的 `hookupMethod` 要么是 `PropertySet`、要么是 `Nothing` ⇒ `hookupMethod.MethodKind = SharedConstructor` 对模块恒不成立 ⇒ 第 2 条对模块恒不命中。

**一句话**：模块不是"更惰性"，而是**没有那个形状**——它连"宿主是自己的 `.cctor` 的 `Handles`"都写不出来，所以它的 `.cctor` 永远只是字段初始化器，永远 `Return True`。类与提交类才可能触发第 2 条。这是判据的结构后果，不是偶然。

实测佐证（跑出来的）：T1-12 模块 + `Handles MyClass.Ev` ⇒ 发射前就被 BC31418 挡掉；T1-13 模块 + `WithEvents` 挂钩 ⇒ `beforefieldinit=True`（宿主是 setter）；T1-8 模块只有字段初始化器 ⇒ `True`；T1-14 模块显式 `Sub New` ⇒ `False`（走第 1 条）。对照类侧：T1-7 / T1-11（`Class` + `MyClass.Ev` + 共享挂钩）⇒ `False`（走第 2 条）。

## 八、异步侧的范围声明（四种形状未测，显式登记为已裁边界）

**跑出来的**：上一轮 T4 测了四种异步形状，都没有出现挂钩丢失——顶层 `Await` 得到事件持有者后由 `WithEvents` 挂钩、`Await` 之后再 raise；共享 `.cctor` 挂钩 + `Await` 后 raise；`Task.Run` 里跨线程 raise。三种都实测可用。

**没测的四种形状（已裁边界，不许悄悄略过）**：

1. **处理器本身是 `Async Sub`**，且第一次 raise 发生在它的状态机尚未恢复时；
2. **`WithEvents` 变量在多个 `Await` 之间被重新赋值**——挂钩挂在属性 setter 上，重新赋值换掉宿主对象，旧对象上仍留着委托；
3. **提交体返回之后仍在飞行的后台任务发起 raise**（此时链已经进入下一条提交）；
4. **并行/竞态**：另一个线程在提交体第一句之前就 raise。

另有一条**语言规则**而非时序：`Shared WithEvents x As T = Await ...` 被 **BC37341** 硬拒（"'Await' cannot be used in a shared field or property initializer because it runs in the shared constructor, which is synchronous."）。拒它的是共享初始化器的同步性规定，不是初始化时机，**不得**记成 T4 的时序结论。

⇒ 边界声明：关于"`.cctor` 承载共享挂钩"的时机结论（T1 / T2 / T3，以及本轮补的五条回归钉）**只覆盖同步的提交形状**。上面四种形状**未测**；**不得**由"T4 通过"推出"异步没有结构性问题"。要动它们须另立测量。

