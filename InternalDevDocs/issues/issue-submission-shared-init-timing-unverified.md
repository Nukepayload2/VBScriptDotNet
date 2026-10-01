# issue 36：提交类共享 `Handles` 挂钩挂在 `.cctor` 上，但"初始化时机"这件事没有任何实测保证

- **登记日期**：2026-09-24（main）
- **状态**：**Open**（下面 §一 的三条代码事实已核；§二 的两格实测未跑 ⇒ 本条现在是"缺保证"，不是"已确认坏"）
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
