# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。我们本周处理动态编程增强（`proposal-any-pseudotype` / `proposal-typeless-declarations` / `proposal-default-methods` / `proposal-async-sub`）这一组建议里的 `Default` 方法。这场讨论在几条线上都相当不舒服：建议原文把至少四件彼此独立的事捆在一起，其中一件（动态加方法）我们怀疑根本不是语言特性、另一件（带名实参元数据）我们读了两遍也说不清它要什么。但剥掉捆绳之后，`Default Sub Invoke` 这颗核心是真东西，而且它踩在 VB 自己的"默认成员"基因上——所以我们没有一拒了之。我们把它拆开谈，然后对每一块给出不同处置。

## Agenda

* [Proposal: `Default` 方法（可调用对象与晚期绑定增强）](#proposal-default-方法)

## Proposal: `Default` 方法

_Related: vblang #135/#136（`Dynamic` 伪类型）、#137（晚期绑定成员访问）；ModVB：`proposal-any-pseudotype`、`proposal-typeless-declarations`、`proposal-delegate-enhancements`_

### 场景与缺口

We started from a genuinely reasonable pain. 存储过程/函数被包成对象之后，今天必须写显式方法名才能调用：

```vb
' 今天：
sp_Users_Initialize.Invoke(...)      ' 或 .Call(...)，样板且暴露实现细节
```

We want to be able to write:

```vb
Dim sp_Users_Initialize As StoredProcedureInfo = ...
sp_Users_Initialize()                ' 直接可调用，无需 .Invoke
```

Anthony 的原文（第 10 章 "Dynamic Programming Enhancements"，`..\AnthonyDesign_wordpress.txt` L1654–1712）把这件事和另外三件事并列在同一段代码块里：动态加方法（`obj.Multiply = Function(left, right) left * right`）、把带名实参信息原样传给目标方法（`sp_Users_SelectById(id:=Guid.Empty, role:="admin")`）、以及晚期绑定下的 `AddHandler` / `RemoveHandler` / `AddressOf`（其中 AddressOf 还附注 "Not shown: Late-bound AddressOf bug fix from Dev10"），再往下是 "Maybe: Await / For Each / Queries"。原文第 16 章优化清单还有一句关键的话："Generalize delegate `Invoke` syntax. (May just be `Default` method attribution)"（L2644）——这告诉我们 `Default` 的原始动机是**把委托的 `Invoke` 语法推广到用户类型**。这是本建议最准确、也最有 VB 味儿的表述。

### 候选方案

**PROPOSAL A — 只做"可调用对象"：`Default` 方法。** `Default Sub Invoke(ParamArray args As Object())` 把一个方法标为默认调用入口，实例 `sp()` 等价于 `sp.Invoke()`。只覆盖原文第一段。

**PROPOSAL B — `Default` 方法 + 晚期绑定下的默认调用。** 在 A 之上，要求 `Any`/动态类型实例同样按"默认方法"分派调用（原文："早期绑定时按 `Default` 方法调用；晚期绑定时同样按默认方法分派"）。这使 `Default` 方法的完整价值需要 `proposal-any-pseudotype` 落地才能兑现。

**PROPOSAL C — 原建议全捆绑。** A + B + 动态加方法（`obj.Multiply = Function(...)`）+ 带名实参元数据（`sp_Users_SelectById(id:=..., role:=...)`）+ 晚期绑定事件/委托（`AddHandler`/`RemoveHandler`/`AddressOf`）+"Maybe"清单（`Await`/`For Each`/`Queries`）。

### 权衡：Q&A

- **C 的捆绑成立吗？** 不成立。四件东西共享"动态"一词，但实现面完全不同：`Default` 方法动的是**调用表达式绑定规则**；动态加方法是 **DLR 分派**；带名实参元数据要么是"晚期绑定带名实参"（真实主线 2017.08.09 刚把它定为编译期错误，见下）要么是反射式元数据捕获（一个全新的、属于 `CallerArgumentExpression` 领地的东西）；晚期绑定事件动的是**运行时 binder**。把四件捆在一起，一份提案的边界就消失了。**结论：按 PROPOSAL A/B/C 拆层讨论，最终逐块处置。**
- **A vs B：`Default` 需要晚期绑定吗？** 需要回答"给谁用"。纯早期绑定的可调用对象（如 `StoredProcedureInfo`）服务的是 DSL/伪动态 API 场景；晚期绑定场景（`Any`/动态）服务的是脚本式对象。两者客户不同、实现面不同。B 依赖 `Any` 落地，而 `Any` 自己还在未决状态（见 vblang 2017.08.23 对 `Dynamic` 伪类型的讨论：只有问题、没有答案）。**倾向：A 先行，B 挂到 `Any` 的后续工作项。**
- **这是不是"第二种做事方式"？** 是，也不全是。`sp()` 与 `sp.Invoke()` 确实并存——但 VB 从来就允许委托 `del()` 与 `del.Invoke()` 并存，且 `Default Property` 与 `obj("key")` 就是"无名索引"的既有先例。所以"第二种方式"的账不是从零欠下的，是从"委托调用"这条既有语义线上延伸出来的。**这减轻了原则 #3 的指控，但没有免除它。**
- **命名与生态撞车。** "Default" 在 .NET 术语里已经超载：`Default Property`、C# 的 Default Interface Members（vblang 2018.02.21 专门讨论过 DIM 对 VB 的严肃影响）、`default` 值语义。再给"可调用对象"贴 `Default` 标签，搜索、文档、新手理解都要多付一笔税。**"Default 方法"这个名字要审，但 `Default` 关键字本身是现成的，我们不换关键字、只审概念名。**

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`Default Sub Invoke(...)` 的声明语法本身无歧义——`Default` 修饰符今天已存在（`Default Property`），把它放到 `Sub`/`Function` 上是增量。真正的歧义在**调用点**。

VB 规范（`..\..\vblang\spec/expressions.md` §Invocation Expressions）今天说：调用目标 "must be classified as a method group or a value whose type is a delegate type"。而 `e(...)` 的消歧，nameof 规范里写过一句经典的话（LDM 2014.10.15/23）：

> In VB, e(…) might be either a method invocation, a delegate invocation, an array indexing, a property access, a default property access, or an invocation of a parameterless function "e" followed by indexing. Which one of these it is, is determined by symbol resolution.

`Default` 方法要给这串列表再加一项"默认方法调用"。消歧顺序必须定死。最大雷区是 **`Default Function`（有返回值）与 `Default Property`（索引器）**：

```vb
Class C
    Default Public Property Item(i As Integer) As String
        Get
            Return "x"
        End Get
        Set(value As String)
        End Set
    End Property
End Class

Dim c As C = New C()
Dim s As String = c(0)      ' 今天：默认属性索引。加了 Default 方法后，c(0) 是谁？
```

若允许"一个类型同时有默认属性与默认方法"，`c(0)` 的绑定就要在两个候选之间再走一层重载——语义负担重且对新手不可见。**v1 规则候选：一个类型至多一个 `Default` 成员（属性或方法），二者互斥。** 这把消歧从"重载"降级为"排他"，是唯一我们愿意下注的 v1 形状。

无参 `Default Function` 的**裸名重分类**（`Dim x As Double = calc` 把 `calc` 重分类为 `calc()`）今天对方法组存在（spec §Expression Reclassification："a method group can be reclassified as a value ... `f` is interpreted as `f()`"），但对"值"目标扩展这条规则会与"默认属性"正面相撞——`obj` 裸名到底取值还是取调用？**v1 明确禁止 `Default Function` 的裸名重分类；`Default Function` 必须带括号调用。** 或者更保守：v1 只放 `Default Sub`，`Default Function` 挂后续。We haven't fully settled; 这是 OPEN QUESTION。

#### 2. 角案例与边界语义

**多 `Default` 重载。** 原文未决问题之一。委托 `Invoke` 可以重载吗？可以（`Func` 不能，但自定义委托可以）。所以 `Default Sub Invoke(x As Integer)` 与 `Default Sub Invoke(x As String)` 并存、`sp(1)` / `sp("a")` 各自命中——可行，重载解析照旧。但**多个不同名的 `Default` 方法**（`Default Sub Invoke()` + `Default Function Run()`）应被禁止：调用点 `sp()` 无法知道该选谁，也不能靠重载消歧（签名可能完全不同）。**v1：至多一个 `Default` 方法（允许同名重载组）。**

**`ParamArray` 与可选参数。** `Default Sub Invoke(ParamArray args As Object())` 是最自然的核心签名，调用点 `sp(1, "a", Nothing)` 原样进 `args`。可选参数组合（`Optional` + `ParamArray`）走既有重载/参数填充规则，无新问题。真正的成本在**晚期绑定**：运行时 binder 面对 `args` 的参数个数/类型只能做运行时重载，`ParamArray` 展开的规则要单独定义。`Suspect`：这是本建议文档完全没碰、却最可能吃掉实现预算的地方。

**泛型 `Default` 与 `ByRef`。** 原文未决问题："可调用对象的泛型 `Invoke`、`ref`/`out` 参数如何表达"。调用表达式文法（`Expression ( OpenParenthesis ArgumentList? )?`）对**值**目标没有类型实参列表位——`sp(Of Integer)(x)` 不解析。泛型 `Default` 方法要么借方法组语法（需要先重分类，目前只对"方法组"有类型实参），要么 v1 禁止。`ByRef` 参数早期绑定无障碍（`sp(x)` 走既有 copy-in/copy-out）；晚期绑定下由运行时 binder 处理，与 2016.05.06 讨论的 ByRef 返回/晚期绑定交互同域（"Passing a ByRef return to a late-bound method call? … Make it work."）。

**继承。** `Default Overridable Sub Invoke(...)` 让基类声明一个"可调用协议"、派生类改写——这其实是 F# 式的 callable protocol，有价值，但 `Overridable`/`MustOverride`/`NotOverridable` 与 `Default` 的组合矩阵要写进 spec。**v1 可以只允许非 Overridable，把虚拟变体列后续。**

**`Await` / `For Each` / `Queries`（原文 "Maybe"）。** `Await sp()` 要求 awaitable 模式在动态下可运行时解析（C# 有 `await` on dynamic 先例，VB 无）；`For Each` 晚期绑定今天靠运行时枚举已可用（`IEnumerable` 反射），但"可调用对象产生序列"需要 `Default` 方法参与查询模式绑定；`Queries` 更远。**三件都 Table，且不得阻塞 v1。**

#### 3. 作用域与绑定

早期绑定时 `sp_Users_Initialize()` 应绑定到 `Invoke` 方法，语义模型里调用表达式 `GetSymbolInfo` 返回 `Invoke` 符号（如同 `del()` 绑定到委托的 `Invoke`）；`sp_Users_Initialize` 表达式本身仍是 `StoredProcedureInfo` 类型。注意规范里的一句话："The default property can only be referred to using the default property access syntax; the default property cannot be referred to by name"（spec §Default Query Indexer 附近）——`Default` 方法应照此办理：**默认方法只能以调用语法 `sp()` 触达，不能 `sp.Invoke` 之外再取"默认"名字**（`Invoke` 仍是它的真名，`sp.Invoke(...)` 永远合法）。这给 IDE 一个干净的展示面。

#### 4. 与既有特性的交互

- **委托调用。** `del()` 今天已等价 `del.Invoke()`。"值类型的 `Default` 方法"不过把这条规则从委托类型推广到任意类型。**实现上最省的做法就是把"含 `Default` 方法的类型"在调用点重分类为"该默认方法的方法组"**，与委托的既有路径共用。这是全特性最亮的一点：不新造绑定机制。
- **默认属性。** 见 Q&A 消歧。v1 排他规则下，`obj(...)` 先查默认属性、再无则查默认方法，顺序明确。
- **表达式树。** 可调用对象调用进表达式树（`Expression(Of ...)`）时，`sp()` 应生成 `Invoke` 的 `Call`/`Invoke` 节点。若 `Default` 方法带 `ParamArray`，表达式树降级为普通数组参数，无新问题。**但动态调用无法进表达式树**（与既有 dynamic 限制一致）。
- **`AddressOf` 取"可调用对象"的地址。** `Dim d As Action = AddressOf sp` 是否把可调用对象隐式转成委托？原文没提；我们**不主动做**——隐式对象→委托转换是另一个特性（delegate-enhancements 域），列 OPEN QUESTION。
- **`AddHandler`/`RemoveHandler`。** 对早期绑定的事件照旧；本建议要的是**晚期绑定**下的事件挂接——见第 5 项。

#### 5. Breaking change 与兼容性

核心部分**几乎零破坏**，这是 `Default` 方法最有说服力的地方：

- 声明侧：`Default` 修饰方法今天是编译错误，所以**没有任何现有代码**会因"合法声明变合法"而受影响。
- 调用侧：`sp()` 在 `Default` 方法引入前对 `StoredProcedureInfo` 是绑定失败；引入后是绑定成功。这是纯 **enable-type** 变化——先前能编译的代码，一个都不改变绑定。
- 交叉程序集：库新增 `Default Sub Invoke` 后，既有 `sp.Invoke(...)` 调用不受影响；`sp()` 从"错误"变"可用"。可接受。

破坏性风险全部集中在捆绑件上：

- **动态加方法 / 带名实参元数据**若改变既有动态代码的绑定（如 `obj.Multiply = ...` 从"普通赋值"变"成员注入"），就是隐蔽语义变化——原则 #7 的红线（"Control flow would be altered by a very subtle character"是 #167 `Return?` 被拒的同一理由）。这也是我们坚持把这两件**拆出本建议**的原因之一。
- **晚期绑定 `AddressOf` 的 Dev10 bug fix**：bug fix 本身是 enable-type（错误→可用），欢迎；但"修 bug"意味着运行时 binder 行为变化，必须配回归测试矩阵，且要明确这是 **运行时库修复、不是语言特性**。

#### 6. Option Strict / 编译选项分叉

两条路径必须行为一致：

- **`Option Strict On`**：`sp()` 早期绑定到 `Default` 方法，编译期校验、编译期报错。
- **`Option Strict Off` / `Any`**：`obj()` 走晚期绑定。**这里有一个我们反复回访的开放问题**：晚期绑定的"默认方法分派协议"到底是什么？运行时只能看见对象的形状，看不见 `Default` 修饰符。候选：① 映射到 DLR 的 `TryInvoke` 原语（`DynamicObject` 已有）；② 约定"名为 `Invoke` 的成员"；③ 编译器在元数据里放标记、运行时 binder 读取。若走 ②，则晚期绑定下 `Default` 修饰符是**冗余**的（按名字就能找到），`Default` 退化为纯早期绑定亲和工具——这不算错，但要写进 spec 让用户知道两条路径的差异。`Probably`：① 为主、② 为回退，与 `Any` 建议共约定。

#### 7. IDE / IntelliSense

`sp_Users_Initialize()` 的调用提示应显示 `Invoke` 的签名（如同委托调用显示 `Invoke`）；成员列表里"默认方法"不应重复出现两个名字（`sp` 名下只显示一次调用入口）。"至多一个默认成员"规则让 IDE 的展示简单——不存在"默认属性 vs 默认方法同时亮起"的局面。Go to Definition 从调用点落到 `Invoke`。这些都要在原型里验证。

#### 8. 数据 / 普遍性

这是本特性最弱的一环。可调用对象（functor）在 VB 生态里是**罕见模式**：VB 业务开发者用委托/`Func`，几乎不自己定义"可调用类"。`StoredProcedureInfo` 是 Anthony 自己的工具链模式。对照 2018.05.30 主线笔记："the majority of Visual Basic customers … primarily want VB to keep doing what it does now"；对照 #303（空条件 `AddHandler`）的处置——"Really seems like a side case"→ No Plans。我们担心 `Default` 方法同样落在"side case"这一档。**没有量化数据**：没有真实代码库统计，没有用户请求量。`Suspect`：这是真实的 DX 增益，但普遍性存疑。

#### 9. 更简替代

- **显式 `Invoke`/`Call`**（原文 Alternative 1）：零新语法，样板可忍。对低频场景完全够。
- **`Callable` 属性/特性**（原文 Alternative 2）：把 `Default` 换成属性标记，避开关键字超载——但引入"元数据驱动的魔法"，可发现性更差，且不解决 `e(...)` 消歧。**不如 `Default` 修饰符。**
- **委托/`Func` 属性**：`Dim sp As Func(Of Object()) = ...`，`sp(args)` 直接可调用——**这是"第二种做事方式"指控的最强反例**：对绝大多数场景，委托已经把"可调用值"做完了。`Default` 方法只对"需要额外状态/行为的调用对象"才不可替代。
- **DLR/`ExpandoObject` 既有机制**（原文 Alternative 3）：覆盖动态加方法与动态调用，零新语法——所以我们把动态件拆给它。

#### 10. 成本 / 优先级

核心（A）的实现面其实不大：在调用表达式绑定里增加"值类型含 `Default` 方法 → 重分类为默认方法方法组"，复用委托路径；加排他性校验；`Default Function` 裸名重分类禁止规则。**这是"缩场景可落地"的典型。** 但 B（晚期绑定协议）、C（动态加方法/带名实参/晚期绑定事件）各自是独立实现面，全部铺开等于四个特性一个季度的预算。**"Fantastic idea, and too hard to do" 不是我们的评语——我们是"好点子，先拆后做，别捆着上"。**

#### 11. 运行时 / CLR 硬约束

无新 IL、无 PEVerify 问题。早期绑定 `sp()` → 编译期改写为 `sp.Invoke()` 的 `call`/`callvirt`。晚期绑定走运行时 binder/DLR，`TryInvoke` 是既有原语。真正的硬约束在**晚期绑定事件**：DLR 没有事件原语，`AddHandler` 晚期绑定需要 binder 自造事件解析——这是成本大头，也是我们把它 Table 的技术理由。

#### 12. 值不值得做

价值（让"调用对象"直接可调，VB 味儿，复用委托 `Invoke` 语义）中低——普遍性存疑；成本（核心 A）中低；风险（核心 A）低（enable-type、无破坏）。**核心 A 值得做，但必须限定 v1 范围，且不得以"全捆绑"形态推进。** 若坚持 C 捆绑，我们会建议不做。

### VB 基因对照

- **不引入"第二种做事方式"（原则 #3）**：这里是本特性最受审的位置。`sp()` 与 `sp.Invoke()` 并存、`sp()` 与委托 `del()` 并存——都算"第二种方式"。辩护只有一条：VB 早已允许 `del()`/`del.Invoke()` 并存，`Default` 只是把这条既有语义线延长一格。**账从既有线起算，不是从零起算，但仍是加法。**
- **消除常见样板（原则 #9）**：对 DSL/伪动态 API 场景，`sp_Users_Initialize()` 省掉 `.Invoke`——样板确实消了，但场景不"常见"（见数据/普遍性）。
- **避免隐蔽的语义变化（原则 #7）**：核心无（enable-type）；捆绑件有（动态加方法、带名实参若改绑定）。**捆绑件被拆走，正是为了保住这条原则。**
- **读起来像英语、对新手友好（原则 #5）**：`Default Sub Invoke(...)` 声明、`sp()` 调用，读起来自然；但"默认方法 vs 默认属性"的区分为新手增加了一个需要内化的概念边界。
- **冗长只在有用时是美德（原则 #10）**：`Default` 关键字是现成的、`Invoke` 是显式的——没有发明新词，加分。
- **与主线关系（对照表 2.3）**：可调用对象在主线**无对应物**（C# 无用户可调用对象语法，VB 主线无 issue）→ **Anthony 独立延伸**。晚期绑定件与主线 `Dynamic` 伪类型（#135/#136）同域但不同物，主线至今只有开放问题没有决议。与 `proposal-any-pseudotype`（ModVB 内）**强依赖**——`Default` 的晚期绑定价值要等 `Any` 落地才兑现。与 `proposal-delegate-enhancements` 共享"委托 Invoke 泛化"这个源头（Anthony 原文把两处都挂在 delegate Invoke 上）。

### RESOLUTION:

1. **拆件处置，不整体采纳。** 原建议捆绑的四件（`Default` 方法 / 动态加方法 / 带名实参元数据 / 晚期绑定事件委托）是四个独立特性，边界必须分开。
2. **PROPOSAL A（`Default` 方法）原则上采纳，限定 v1**：声明 `Default Sub Invoke(...)`、调用 `sp()` 早期绑定改写为 `sp.Invoke()`；复用委托"值→`Invoke` 方法组"的既有重分类路径，不新造绑定机制。
3. **v1 排他规则**：一个类型至多一个 `Default` 成员（属性或方法，二者互斥）；至多一个 `Default` 方法（允许同名重载组）；**v1 只放 `Default Sub`**，`Default Function` 及其裸名重分类列后续（OPEN QUESTION）。
4. **PROPOSAL B（晚期绑定默认分派）挂到 `Any`**：随 `proposal-any-pseudotype` 落地，协议以 DLR `TryInvoke` 为主、按名字约定 `Invoke` 为回退；不在本建议内独立实现。
5. **动态加方法（`obj.Multiply = Function(...)`）不是语言特性**：`Any` + `ExpandoObject` + DLR 即可，本建议零新增语法；从本建议移除，归属 `proposal-any-pseudotype`。
6. **带名实参元数据（`sp_Users_SelectById(id:=..., role:=...)`）语义不明**：`Suspect`。早期绑定 `:=` 早已支持；晚期绑定带名实参是 2017.08.09 明确设定的编译期错误（"Late-bound method invocations which have non-trailing named arguments raise a compile-time error"）。"把参数名信息原样传给目标方法"若指反射式元数据捕获，需另立建议（`CallerArgumentExpression` 领地）。**Table。**
7. **晚期绑定事件（`AddHandler`/`RemoveHandler`/`AddressOf`）**：Dev10 的 `AddressOf` 修正是运行时 binder bug fix——以 **运行时库修复**处理（enable-type，配回归矩阵）；晚期绑定 `AddHandler`/`RemoveHandler` 因 DLR 无事件原语，**Table**（真实主线先例：#303 空条件 AddHandler 已标 "LDM Reviewed: No Plans"，我们同样不认为这是高频场景）。
8. **"Maybe" 清单（`Await`/`For Each`/`Queries`）**：一律 Table，不阻塞 v1。

### Implication:

- 起草最小 speclet：调用表达式绑定规则扩展（"含 `Default` 方法的类型"重分类）、排他性校验、`Default Function` 裸名禁令、与默认属性的消歧顺序。
- 撰写 Compatibility 分析：声明侧 enable-type 论证、交叉程序集行为、`Option Strict` 两条路径的协议差异（`TryInvoke` vs 名字约定）。
- 与 `proposal-any-pseudotype` 团队对表：确定晚期绑定默认调用协议归属、`TryInvoke` 回退链。
- 把"晚期绑定 `AddressOf` Dev10 bug fix"转成运行时 binder 的独立工作项，注明原文 "Not shown" 的具体问题与方案缺失。
- 未决问题移交 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：`Default Function`（有返回值）是否允许、裸名重分类的精确规则；`Default Sub` 与 `Default Function` 能否并存（原文未决问题）。
- `OPEN QUESTIONS`：晚期绑定"默认方法分派协议"的确切定义（`TryInvoke` 为主、名字约定为回退？），待 `Any` 决议。
- `OPEN QUESTIONS`：泛型 `Default` 方法如何表达（调用点类型实参文法缺失）；`AddressOf sp` 是否隐式转委托。
- `OPEN QUESTIONS`：带名实参元数据到底指什么——晚期绑定带名实参？还是反射式元数据捕获？需要 Anthony 澄清或新建议。
- `TODO`：为"可调用对象"普遍性补证据——真实代码库里 `Invoke`/`Call` 显式调用的频次、DSL 场景用户量。无数据则维持 `Suspect`。
- `Follow-up`：与 `proposal-delegate-enhancements` 共享"泛化委托 Invoke"源头的两建议，统一术语（避免"默认方法"与"委托 Invoke 泛化"两套说法）。

### 状态

- **LDM 状态：Consider**。核心子特性 `Default Sub Invoke`（限定 v1）**Active**；捆绑件逐一拆走/Table。整体建议按原文形态**不予推进**。
- **三态判定：Consider** — 核心价值真实、范围可缩、无破坏；但普遍性存疑、捆绑件边界模糊、晚期绑定协议未决。拆件重写后再审，可升 Active。

---

## 附录：特性评价

# 建议评价报告：proposal-default-methods.md

## 评价对象

- 建议：proposal-default-methods.md — `Default` 方法（可调用对象）+ 动态加方法 + 带名实参元数据 + 晚期绑定事件/委托
- 来源：Anthony 原文第 10 章 "Dynamic Programming Enhancements"（`..\AnthonyDesign_wordpress.txt` L1654–1712）；"Generalize delegate Invoke syntax. (May just be Default method attribution)"（L2644，第 16 章优化清单）
- 配方目标：让"代表存储过程/函数"的对象可调用、动态给对象加方法、伪动态 API 带名实参直传、晚期绑定下支持事件与委托

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。核心 `sp_Users_Initialize()` 可直接演示；但"晚期绑定默认分派"依赖未落地的 `Any`，带名实参部分语义不明无法演示，动态加方法经 DLR 已可用（非本建议新增） | 已检查（核心语法未定型 → 封顶 3） | 无原型/运行结果；5 个未决问题含核心语义（Default Function 并存、泛型 Invoke、ref/out）；Motivation 把四件事并列，主效果边界不清 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。`Default` 关键字复用、委托 `Invoke` 泛化是纯 VB 基因；但捆绑了 DLR 动态件（外来）与元数据捕获（外来），构成"打包次要无关能力" | 已检查 | 动态加方法/带名实参是外来件且未做 VB 化说明；`Default` 术语与 Default Interface Members（主线 2018.02.21 讨论过）生态撞车未识别 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、示例与原文逐字一致、Drawbacks 诚实；但四特性捆绑边界模糊（红旗 #8）、无文法/无 Compatibility 章节、状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`，红旗 #6）、"Not shown"/"Maybe" 未定性 | 已检查 | 核心"调用点消歧（Default 方法 vs Default 属性 vs 委托）"未展开；`Default Function` 裸名重分类未提及；Compatibility 分析缺 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。雷/光正向（DSL 提速、VB 差异化于 C#）；风受损（`Default` 术语撞车、与 `Any` 强耦合未声明）、暗风险（晚期绑定事件动 binder、动态件改绑定的隐蔽语义变化）未对冲 | 已检查（预测待定） | 未权衡"第二种做事方式"对扩展表面的长期成本；动态/晚期绑定件的 breaking 风险未识别；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。核心材料（VB `Default` 属性、委托 `Invoke`）来自 VB 基因但未点明；动态件借用 DLR/C# dynamic 未标注；"Maybe" 清单（Await/For Each/Queries）源自原文但影响未估 | 已检查 | 四件来源各异（VB6 默认成员 / DLR / 运行时 binder bug fix）未区分；借鉴 C#/DLR 未声明；无杂质但成分混装 |

## 设计原则对照

- **与 VB 基因：部分一致，部分偏离。** 一致：`Default` 关键字现成、`Invoke` 显式（#10）、可读（#5）、核心 enable-type 无破坏（#1）。偏离：`sp()` 与 `sp.Invoke()`、`sp()` 与委托 `del()` 构成"第二种做事方式"（#3），虽从既有语义线起算仍是加法；捆绑动态件触碰原则 #7（隐蔽语义变化）红线。
- **与主线关系：Anthony 独立延伸。** 可调用对象在 vblang 主线无对应物；晚期绑定件与主线 `Dynamic` 伪类型（#135/#136）、晚期绑定成员访问（#137）同域但主线仅有开放问题；与 `proposal-any-pseudotype`（ModVB 内）强依赖。术语 `Default` 与主线刚讨论的 C# Default Interface Members（2018.02.21）撞车。
- **破坏性变更：核心无**（`Default` 修饰方法今天即编译错误；调用点 enable-type）；**捆绑件潜在有**（动态加方法/带名实参若改变既有动态绑定 = 隐蔽语义变化；晚期绑定 `AddressOf` bug fix 改变 binder 行为，属运行时修复需回归矩阵）。

## 总评

- **达成程度：部分达成**——核心"可调用对象"概念与 VB 基因成立且无破坏；但捆绑件边界模糊、晚期绑定协议未决、普遍性无数据。
- **LDM 三态建议：Consider**——拆件重写后，`Default Sub Invoke` 核心（限定 v1：至多一个 `Default` 成员、仅 `Default Sub`、排他于默认属性）可升 Active；动态加方法/带名实参/晚期绑定事件各归各的工作项（Table / 归属 `Any` / 运行时库）。
- **主要问题**：① 四特性捆绑，边界消失；② 晚期绑定"默认分派协议"未定义且依赖未落地的 `Any`；③ 带名实参元数据语义不明（`Suspect`）；④ 调用点消歧（默认方法 vs 默认属性 vs 委托）未展开；⑤ 普遍性无数据支撑，可能落入 "side case"（对照 #303 → No Plans）。

## 返工建议

- **补充章节**：拆分建议——本建议只留"`Default` 方法（可调用对象）"；新增 Compatibility 章节（声明侧 enable-type、交叉程序集、`Option Strict` 分叉）；新增文法/绑定规则（调用表达式重分类、"至多一个 `Default` 成员"排他校验、`Default Function` 裸名禁令）；补"调用点消歧顺序"图。
- **补充证据**：最小原型（`Default Sub` 早期绑定 + 排他校验 + 语义模型 `GetSymbolInfo` 与 IDE 调用提示）；"可调用对象"普遍性数据（真实代码库 `Invoke`/`Call` 显式调用频次、DSL 场景用户量）。
- **未决问题处理**：`Default Function` 并存与裸名重分类 → v1 禁、后续开；泛型 `Default` 与 `ByRef` → 列 spec 边界；`AddressOf sp` 隐式转委托 → 明确不做；晚期绑定协议 → 移交 `Any` 工作项；带名实参元数据 → 需 Anthony 澄清或另立建议。
- **设计探索**：与 `proposal-delegate-enhancements` 统一"泛化委托 Invoke"术语；`Default` 概念命名再审（避开 Default Interface Members 撞车）；晚期绑定事件若坚持要做，先评估 DLR 事件原语的成本与替代（运行时 binder 事件解析 vs 名字约定）。

---

## 附录：C# 生态与互操作考量

> 本附录为 ModVB 评估追加，依据 `..\..\csharplang`（dotnet/csharplang 官方镜像，main 分支）与 `..\..\csharplang-index.md`（索引，主题 T7/T8/M7 相关）。C# 原文逐字引用并标注来源文件；无法核实处标 **Suspect** / **OPEN QUESTIONS**。本提案（`Default` 方法 / 可调用对象）与 C# 生态的关系是**中等的、且互操作面干净**：核心特性在元数据层不可见，真正的生态触点集中在三处——①「`Default` 术语与 C# 8 Default Interface Methods 撞车」；②「晚期绑定分派协议与 C# `dynamic`/DLR 的关系（方向相反但可共享基础设施）」；③「C# extensions 对『给类型加行为』的演进 vs 本提案的『类型内默认调用入口』」。按「现实方向 → 判定 → 适应建议 → 对 RESOLUTION 影响」展开。

### 相关 C# 现实方向

本提案主题在 C#/CLR/.NET 生态中的对应走向，集中在以下四条。

**1. C# 8 Default Interface Methods（DIM）：C# 把 "default" 一词钉在「接口默认实现」上。**

C# 8 的 DIM 让接口可以自带方法实现，API 作者演化接口而不破坏既有实现者。Summary 逐字：

> "Add support for _virtual extension methods_ - methods in interfaces with concrete implementations. A class or struct that implements such an interface is required to have a single _most specific_ implementation for the interface method, either implemented by the class or struct, or inherited from its base classes or interfaces. Virtual extension methods enable an API author to add methods to an interface in future versions without breaking source or binary compatibility with existing implementations of that interface." → `proposals\csharp-8.0\default-interface-methods.md`（Summary）

它要求 CLI/CLR 配合，且新平台特性不可跑在旧平台：

> "(Based on the likely implementation technique) this feature requires corresponding support in the CLI/CLR. Programs that take advantage of this feature cannot run on earlier versions of the platform." → 同上（Summary）

DIM 与本提案**语义无关**：DIM 是「接口能自带实现、演化不破坏实现者」，本提案是「类型可被调用」；撞车只在 `Default` 这个词。另注意 DIM 的 base interface invocation（`base(Interface).M()`）在 C# 8 **未实现**：

> "This decision was not implemented in C# 8. The `base(Interface).M()` syntax is not implemented." → 同上（Resolved Questions → Base Interface Invocations (closed)）

对 VB 的既有关口在**消费侧**：VB LDM 早在 2018.02.21 就警告过接口演化对 VB 的严肃影响——

> "The implication is that people will then evolve interfaces, and if VB can't handle this, it will have a serious problem." → `..\..\vblang\meetings\2018\vbldm-notes-2018.02.21.md`（DIM 议程，No action）

这是**独立于本提案的既有互操作缺口**（VB 编译器须能识别接口默认实现的元数据），不是本提案引入的。

**2. C# 14/15 extensions：C# 「给既有类型加成员」的官方答案（从外部附加）。**

C# 14 落地的 extensions 让静态类里声明「扩展块」，为任意接收者类型添加命名成员（方法/属性/运算符），C# 15 续推。声明语法逐字：

> "Extension declarations shall only be declared in non-generic, non-nested static classes." → `proposals\csharp-14.0\extensions.md`（Declaration → Syntax）

> "An extension declaration is anonymous, and provides a _receiver specification_ with any associated type parameters and constraints, followed by a set of extension member declarations." → 同上（Extension declarations）

降级是「原样调用静态实现方法」，与 classic extension methods 同构（其他编译器可消费其元数据）：

> "Whenever extension members are used in source, we will emit those as reference to implementation methods." → 同上（Lowering）

扩展成员**不允许虚/抽象/重写**——它是纯静态、从外部附加的成员，不改变类型的虚分派表：

> "It is an error to specify the following modifiers on a member of an extension declaration: `abstract`, `virtual`, `override`, `new`, `sealed`, `partial`, and `protected` (and related accessibility modifiers)." → 同上（Extension members）

C# 15 节奏（Kickoff）：

> "C# 14 will deliver the next iteration of extension members, but we did not get to all the member types we want yet. We expect to turn the crank on this in C# 15." → `meetings\2025\LDM-2025-08-18.md`（Extensions）

**关键对照**：C# extensions 是「**从外部**给类型加**命名**成员」；本提案的 `Default` 方法是「**在类型内部**声明一个**调用入口**」。两者正交——extensions 造不出 `sp()` 语法（C# 无用户可调用对象语法，delegate 是唯一「可调用值」），`Default` 方法也不新增命名成员（`Invoke` 仍是真名，正文 §作用域与绑定已述）。ModVB 侧真正的 extensions 对位是 `proposal-extension-properties`（`Module` + `<Extension>`），与本提案分属两条线。

**3. C# `dynamic` / DLR `TryInvoke`：晚期绑定分派的共享基础设施，但 C# 正撤出动态面。**

本提案 B（晚期绑定默认分派）与 C# `dynamic` 共用 DLR，`DynamicObject.TryInvoke` 是既有原语（正文第 6 项已列）。C# 侧动态面长期无大演进，且受 AOT/trimming 压力（索引 T7）：unsafe-evolution 的 open question 甚至问过「dynamic 是否应标 unsafe」；C# 14 只在表达式树里放宽了可选/具名参数，未动 dynamic 本体。方向本质：**动态/晚期绑定不是 C# 的前进方向**，C# 靠类型系统与 source-gen 取代之。正文「Maybe」清单引用的「C# 有 `await` on dynamic 先例」（C# 5 起 `GetAwaiter` 按成员名动态分派）属实，但 csharplang 仓库无本轮逐字出处，标 **Suspect**（作为背景，不影响结论——「Maybe Await」已 Table）。

**4. ref struct 接口与 DIM 的边界（M7 相关）：C# 13 明确「ref struct 不可实现带 DIM 的接口」。**

> "default interface methods imply a receiver of interface type, which is a non-value type and violates rule (3). Thus, default-interface-members are disallowed." → `proposals\csharp-13.0\ref-struct-interfaces.md`（设计：rules (4) and (5)）

这条与本提案无直接关系，但佐证「default」语义在 C# 生态里已被 DIM 及其边界规则充分占据——VB 用同一词指「可调用对象」，须在文档/搜索层面明确区分。

### 现实 vs 提案

| 本提案要点 | C# 现实方向 | 判定 | 理由 |
|---|---|---|---|
| 核心：`Default Sub Invoke` + `sp()` 早期绑定改写为 `sp.Invoke()` | C# 无用户可调用对象语法；委托 `del()` 是唯一「可调用值」 | **兼容（互操作面干净）+ 脱节（无 C# 对位）** | 元数据不可见：`sp()` 就是 `call`/`callvirt` 到 `Invoke`，C# 消费方只见普通 `Invoke` 方法，零桥接、零冲突；但 C# 无对应语法，是纯 VB 差异化（Anthony 基因，正文 §VB 基因对照已述） |
| 术语 `Default`（概念名） | C# 8 "Default Interface Methods" 已占据「default」 | **冲突（术语层）** | 正文 Q&A 已识别（命名撞车）；DIM 是接口演进、本提案是可调用对象，语义无关、词面撞车；不改关键字（`Default` 现成），但概念名/文档须区分 |
| `Default` 排他规则（属性/方法互斥，v1） | C# 索引器 `this[...]` 即 VB `Default Property` 的元数据形态 | **兼容** | v1 排他让元数据故事干净：一个类型要么是索引器、要么是普通 `Invoke` 方法，C# 消费侧无歧义 |
| 晚期绑定默认分派（B，挂 `Any`） | C# `dynamic` + DLR `TryInvoke` 同一基础设施 | **需桥接（方向相反但可共享）** | 协议（TryInvoke 为主、名字约定回退）与 C# dynamic 兼容；但 C# 在撤出动态面（AOT 压力），VB 是「按需动态」差异化——不冲突，须把动态做成 opt-in |
| 动态加方法 / 带名实参（已拆出） | C# 用 source-gen / interceptors 取代运行时动态；无「动态加方法」语言特性 | **脱节（已拆出，无需处理）** | 已从本提案移除（RESOLUTION #5/#6）；`.vbx` 用 `ExpandoObject`/DLR，C# 用编译期生成，双模路线（决策文件 M5） |
| 晚期绑定事件（Table） | C# event 是编译期成员；无 DLR 事件原语 | **兼容（Table 合理）** | DLR 无事件原语与 C# 无动态事件同构；正文 Table 判定被生态确认（vblang #303 → No Plans 同向） |
| 消费 C# DIM 元数据 | C# 接口可自带默认实现且会演化 | **需桥接（既有缺口，非本提案引入）** | vblang 2018.02.21 已标为 VB 的 "serious problem"；`.vbx` 编译器须能消费 DIM 元数据，否则接口演化时 VB 侧断裂（决策文件 M7） |

### 对 VBScript.NET 的适应建议

1. **把「元数据不可见」当第一卖点**：`Default` 只存在于 VB 绑定期，IL 层面就是到 `Invoke` 的普通调用。无新 IL、无新签名、无 PEVerify 问题（正文第 11 项已述），交叉程序集与 C# 消费**零桥接**——这是本提案与 C# 生态关系里最强的正面结论。
2. **默认安全、按需动态**：早期绑定 `sp()` 是编译期重写，AOT/trimming 友好（无反射）；晚期绑定 B 挂到 `Any` 且明确 opt-in，与 C# 弱化 dynamic / AOT 压力同向（索引 T5/T7）——「默认走类型化，动态按需开」是 `.vbx` 对动态面的一致策略。
3. **source-gen 桥留给 extensions 线，不给 `Default`**：C# 用 extensions/source-gen 给类型加命名成员；`.vbx` 侧若做扩展属性（`proposal-extension-properties`）应仿 C# extensions 的 `[Extension]` 元数据以互认（C# 已规定其元数据「其他编译器可以消费并生成」，见 extensions.md Lowering）。`Default` 方法本身不需要 source-gen。
4. **识别 DIM 元数据是共同前提**：`.vbx` 编译器须能消费 C# 8 DIM（接口默认实现、reabstraction、`RuntimeFeature.DefaultInterfaceImplementation` 等），这是既有缺口、独立于本提案；与 M7 / `proposal-delegate-enhancements` 的「识别新元数据」建议合并跟踪。
5. **文档/搜索层划清术语**：凡涉 `Default`，显式标注「与 C# Default Interface Methods 无关」；IDE 内概念名用「可调用对象 / callable object」降撞车。正文 Follow-up「`Default` 概念命名再审」与此一致。

### 对既有 RESOLUTION / 三态判定的影响

- **RESOLUTION #2（核心 A 采纳、限定 v1）不受影响，且获互操作背书**：元数据不可见 → 交叉程序集零破坏（正文第 5 项 enable-type 论证）在 C# 生态侧成立，C# 无新增压力。
- **RESOLUTION #4（B 挂 `Any`，TryInvoke 为主、名字约定回退）维持**：协议与 C# `dynamic` 共用 DLR，兼容；C# 撤出动态面不构成对 B 的否决——晚期绑定是 VB 面向 COM/Office 的差异化强项（决策文件 M2），做成 opt-in 即可。
- **RESOLUTION #6（带名实参 Table）、#7（晚期绑定事件 Table/运行时修复）被生态确认**：C# 无动态事件原语、无「动态加方法」语言特性，Table 方向正确；Dev10 `AddressOf` 修复作为运行时库修复（enable-type）与 C# 侧「运行时 binder 属运行时库、非语言」的边界一致。
- **三态 Consider 维持**。补充口径：本提案与 C# interop 关系「中等且干净」——术语撞车是唯一真实生态税，晚期绑定协议方向相反但可共享 DLR；互操作面不构成采纳阻力，也不构成采纳推力。

### 引用纪律与未决项

已核实并可逐字引用的 C# 原文（全部在 `..\..\csharplang` 镜像中核对）：

| 原文（节选） | 来源 |
|---|---|
| "Add support for _virtual extension methods_ - methods in interfaces with concrete implementations..." | `proposals\csharp-8.0\default-interface-methods.md`（Summary） |
| "(Based on the likely implementation technique) this feature requires corresponding support in the CLI/CLR. Programs that take advantage of this feature cannot run on earlier versions of the platform." | 同上（Summary） |
| "This decision was not implemented in C# 8. The `base(Interface).M()` syntax is not implemented." | 同上（Resolved Questions → Base Interface Invocations (closed)） |
| "Extension declarations shall only be declared in non-generic, non-nested static classes." | `proposals\csharp-14.0\extensions.md`（Declaration → Syntax） |
| "An extension declaration is anonymous, and provides a _receiver specification_ with any associated type parameters and constraints, followed by a set of extension member declarations." | 同上（Extension declarations） |
| "Whenever extension members are used in source, we will emit those as reference to implementation methods." | 同上（Lowering） |
| "It is an error to specify the following modifiers on a member of an extension declaration: `abstract`, `virtual`, `override`, `new`, `sealed`, `partial`, and `protected` (and related accessibility modifiers)." | 同上（Extension members） |
| "C# 14 will deliver the next iteration of extension members, but we did not get to all the member types we want yet. We expect to turn the crank on this in C# 15." | `meetings\2025\LDM-2025-08-18.md`（Extensions） |
| "default interface methods imply a receiver of interface type, which is a non-value type and violates rule (3). Thus, default-interface-members are disallowed." | `proposals\csharp-13.0\ref-struct-interfaces.md` |
| "The implication is that people will then evolve interfaces, and if VB can't handle this, it will have a serious problem." | `..\..\vblang\meetings\2018\vbldm-notes-2018.02.21.md`（DIM 议程） |

**OPEN QUESTIONS / 未核实项**：
- C# 5 `await` on dynamic 的 `GetAwaiter` 动态分派：正文提及的先例属实（既定语言行为），但 csharplang 仓库无本轮逐字出处，标 **Suspect**；不影响本附录结论（「Maybe Await」已 Table）。
- extensions 的最终成员种类扩展（C# 15 是否会加入 indexers / nested types 等）未定（`proposals\csharp-14.0\extensions.md` 的 Open issues 仍开放）；若 C# 15 扩大 extensions 覆盖面，对「VB 侧 extensions 对位」的判断需随 LDM 更新。
- VB 编译器（Roslyn VB）当前对 C# 8 DIM 元数据的具体 import 行为未在本仓库核实（**Suspect**：vblang 2018.02.21 讨论后无后续决议记录）。
