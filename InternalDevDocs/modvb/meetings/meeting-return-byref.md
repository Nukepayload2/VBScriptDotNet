# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。今天我们讨论一条小建议——`Return` 语句顺带给 `ByRef`/`Out` 形参赋值。它本身只占 Anthony 原文 3.13 一节三行，但恰好是另一条建议（Out 实参/形参，`proposal-out-arguments.md`）的承载语法：那边所有的示例都写着 `Return True, root:=1`。所以我们把它单独拿出来过一遍，既审它自己，也审它对 Out 参数设计的支撑作用。

## Agenda

* [Proposal: `Return` 赋值给 `ByRef`/`Out` 参数](#proposal-return-赋值给-byrefout-参数)

## Proposal: `Return` 赋值给 `ByRef`/`Out` 参数

_Related: [vblang #305 – In and Out operators](https://github.com/dotnet/vblang/issues/305)（Out 部分：并入模式匹配，LDM Reviewed: No Plans）；[vblang #167 – `Return?`](https://github.com/dotnet/vblang/issues/167)（否决）；[vblang #60 – Out variables](https://github.com/dotnet/vblang/issues/60)；[vblang #124 – Pattern matching](https://github.com/dotnet/vblang/issues/124)；vblang 2014-02-17 LDM 笔记 #42（Out 参数与隐式声明）；vblang 2017-08-23 LDM 笔记（Out Arguments）；vblang 2016-05-06 LDM 笔记（ByRef Returns / Tuples）；ModVB：`proposal-out-arguments.md`（组 9）_

### 场景与缺口

We started from the most ordinary of patterns：函数既要返回结果、又要回填输出参数。`Try*` 家族遍布 BCL，VB 教科书里至今还是先赋值再返回的写法：

```vb
Function TryGetValue(key As String, ByRef value As Object) As Boolean
    Dim result As Object = Lookup(key)
    If result Is Nothing Then
        Return False
    End If
    value = result
    Return True
End Function
```

Anthony 的建议（原文 §3.13）把"写回 + 返回"压成一条语句：

```vb
' `Return` statement may now also assign to `ByRef`/`Out` parameters.
Return True, value:=result
```

We see the gap:返回值与输出参数的写回在语义上属于同一次"交付"，却被拆成两条语句；顺序敏感、读者得读完两行才明白"这次调用交付了什么"。若输出参数多，样板更多。而一旦我们接受 Out 参数建议，`Return True, root:=1` 就是它唯一自然的落地形态——本建议其实是那一条建议的语法子集。

### 候选方案

**PROPOSAL A — 完整版：`Return <表达式> [, name:=<表达式>]...`。** 按建议原文：`Return` 返回函数值的同时，逗号分隔的赋值项依次写回 `ByRef`/`Out` 形参；具名形式（`value:=result`）为主，位置形式（`Return True, a, b` 按形参顺序匹配）也在未决问题里提过。

**PROPOSAL B — 仅 `Out`：只对 Out 形参开放赋值，`ByRef` 维持显式赋值。** 论证是"职责单一"：`ByRef` 读写皆可、语义已由 copy-in/copy-out 覆盖，压缩价值只有省一行；`Out` 只写不读、天生要保证"返回前必赋值"，`Return name:=expr` 是满足 definite-assignment 的天然载体。这正是 2014 年主线对 Out 参数的设想（"Warnings will be emitted for both use-before-assign and return-without-assign"）——本建议正好把 `return-without-assign` 警告的治理手段交到作者手里。

**PROPOSAL C — 仅 `Sub` 写回：`Return name:=expr`（无返回值项）只对 `Sub`/`Async Sub` 开放。** 论证是"缺口最大"：Function 里 `value = result` + `Return True` 只差一行；而 Sub 里"提前退出时写回多个输出参数"要写多条赋值再 `Exit Sub`，压成一条 `Return status:=-1, message:="not ready"` 省得最多，且 `Return` 在 Sub 里本来就允许裸用（无表达式），给 `Return` 挂上赋值是零文法冲突的增量。

**PROPOSAL D — 什么都不做 / 元组替代。** 保持现状；或让 `Return (True, root)` 直接返回具名元组，由 out-arguments 建议或 2016 年的 Tuples 兜底。

### 权衡：Q&A

- **A vs B：`ByRef` 值得一起做吗？** 值得做，但理由不是省一行，而是**一致性**——若 `Return` 能给 `Out` 赋值却不给 `ByRef` 赋值，作者会在两种形参间被迫切换写法。`ByRef` 值类型的 copy-in/copy-out 机制（进入时拷贝到局部、退出时写回调用方）天然兼容：`Return True, value:=result` 只需在 `ret` 之前执行一次普通赋值，copy-out 由既有机制完成，无需新代码生成。`Probably`：A 的地盘覆盖 B，B 是 A 的纪律（只准给只写参数赋值）。我们倾向于 A 的语法 + B 的规则。
- **A vs C：先做哪片？** C 的价值密度更高、与"Return 只表达返回"的语义冲突更小（Sub 里 Return 本来就不返回值，挂上写回不改变 Return 的含义）；A 是 out-arguments 的承载，绕不开。**结论：两片都要，但 C 是独立可交付的第一片，A 随 out-arguments 走。**
- **A vs D：为什么不是元组？** 2016 年主线定 Tuples 时就讨论过"返回多个值"（2014-04-23 笔记的三条丑陋路径：`out` 参数、元组、自定义类型）。元组改变方法签名形状、调用方要解构；`Try*` 风格要求"成功判定 + 值"由同一个调用带出、且失败时值仍要确定——元组做不到 `TryGetValue` 这种形态。**D 不构成替代，A/B/C 是增量。**
- **最大的反对：Return 从此有副作用。** `Return` 在 VB 里向来只做一件事——带着值离开。给它挂上赋值，读者可能忽略被同时写回的参数。这个反对在我们看来成立一半：`name:=expr` 是**显式**的（命名实参语法，VB 人人认识），不是 2018 年主线否决 `Return?`（#167）时担心的"一个细微字符改变控制流"；但括号问题（见下）确实踩了同一类雷。显式性救它，隐蔽性不救它。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`Return True, value:=result` 复用命名实参的 `:=`——这是 VB 基因，不是新符号。真正的歧义在**括号**：2016 年 Tuples 定下具名元组字面量 `M((x:=0, y:=0))`。于是：

```vb
' 若函数返回 (Boolean, Integer)：
Return (True, root:=1)   ' 具名元组字面量 —— 既有 Tuples 语法

' 若函数返回 Boolean 且有 Out root 形参：
Return True, root:=1     ' 本建议：返回 True 并写回 root
```

一个括号，两种完全不同的语义。这就是主线否决 `Return?` 的那类"细微字符改变含义"的形态（#167："Control flow would be altered by a very subtle character"）。`Probably`：可解析——括号是元组文法的组成部分，`Return` 语句的文法可写作"可选表达式 + 零或多个 `name:=expr` 项"，括号立即把整段划进元组；若两种绑定都不成立，则报错。但文法上能分，不代表人眼不会看错。必须写进 spec 并配 IDE 区分着色。位置形式 `Return True, a` 语法安全（VB 无逗号运算符、裸逗号列表今天必是语法错误），但绑定到"第几个 ByRef 形参"的规则要按形参顺序匹配，且 `a` 必须是 lvalue——我们暂缓位置形式（见 RESOLUTION）。

#### 2. 角案例与边界语义

**求值顺序（核心未决）。** `Return <expr>, a:=F(), b:=G()` 的求值顺序必须定义。两种候选语义：

```vb
Function Verify(ByRef value As Integer, ByRef count As Integer) As Boolean
    ' 语义 (i)：先求返回值表达式，再依次执行赋值。
    '          等价于 Dim tmp = (count >= 0) : value = GetValue() : count = count + 1 : Return tmp
    ' 语义 (ii)：先执行赋值，再求返回值表达式。
    '          等价于 value = GetValue() : count = count + 1 : Return (count >= 0)
    Return count >= 0, value:=GetValue(), count:=count + 1
End Function
```

We think (i) is right：`Return expr` 的表达式求值点应与裸 `Return expr` 完全一致，赋值是"退出时的写回"，而不是"返回前的准备"——否则把一条 `Return` 改写成两条语句就会改变结果，违背"写作顺序即执行顺序"的直觉。**`Probably`：(i)**；多赋值项之间左到右（与实参求值一致）。这必须写进 spec，因为 `count >= 0` 与 `count:=count+1` 交错时两条路径给出不同的布尔结果。

**copy-in/copy-out。** `ByRef` 值类型参数在进入方法时拷贝、退出时写回（Fact，VB 既有机制）。`Return True, value:=result` 在 `ret` 前做一次普通赋值即可，copy-out 自动发生——不需要新的代码生成路径。唯一的注意点是：赋值发生在 copy-out **之前**，所以写回一定可见；这与"先 `value = result` 再 `Return True`"完全等价。

**ByVal 形参作赋值目标 = 错误。** 今天方法体内可以给 `ByVal` 参数赋值（它只是局部拷贝），但写回无意义：

```vb
Function Bad(ByVal value As Integer) As Boolean
    Return True, value:=42   ' 错误：ByVal 形参的写回到不了调用方
End Function
```

规则：赋值目标必须是 `ByRef` 或 `Out` 形参；`ByVal` 目标直接报错，不给"无害但误导"留余地。

**可选与未赋值。** `Optional ByRef` 是合法 VB 形参；若某条 `Return` 未提及它，则保持其既有值——允许。但 `Out` 形参不同：2014 年主线定的规则是"Warnings will be emitted for both use-before-assign and return-without-assign"。我们据此定：**凡含 `Out` 形参的方法，每条 `Return` 必须对所有 `Out` 形参赋值，否则编译警告**。失败路径的惯用法就是 out-arguments 建议里的 `Return False, root:=Nothing`。`Optional Out` 与这条规则冲突，`Suspect`：要么禁止 `Optional Out`，要么 `Optional Out` 允许省略（省则用默认值）——待 out-arguments 建议定夺。

**裸 `Return` 与 `Exit Function`。** Function 里裸 `Return` 是编译错误（必须返回值），所以 Function 的 `Return` 第一项必是返回表达式；Sub 里裸 `Return` 合法，`Return name:=expr` 是纯写回（C 片）。`Exit Function` / `Exit Sub` 是否也携带赋值？**Table**——一致性上诱人，但会把特性面翻倍，且 `Exit` 的语义是"立即离开"，挂赋值反而削弱。

**属性 / 字段作目标。** 具名形式 `Return True, Me.Property:=v` 在文法上不成立（`Me.Property` 不是合法命名实参名）；位置形式 `Return True, Me.Property` 可以但被暂缓。结论：v1 只允许形参名作目标。

#### 3. 作用域与绑定

`value:=result` 里的 `value` 解析到**当前方法的 `ByRef`/`Out` 形参**，不是局部变量、不是方法名（VB 遗留的"函数名赋值" `F = 42` 与此无关）。若形参与局部变量重名——VB 不允许形参与局部变量同名（后者遮蔽前者是错误），所以绑定无歧义。语义模型里，Return 语句的赋值项应产生与 `value = result` 相同的符号与数据流（写到形参的存储位置）；Return 语句的返回值项不受影响。这是 bind 层的增量，不是新符号种类。

#### 4. 与既有特性的交互

- **Out 参数建议（`proposal-out-arguments.md`）**：本建议是其承载语法。`Function IsPerfectSquare(number As Integer, Out root As Integer) As Boolean` 配合 `Return True, root:=1` / `Return False, root:=Nothing`，恰好完成"成功判定 + 值提取 + 失败时值仍确定"。**两建议必须同一份 spec、同一原型**，否则语法漂移。
- **Async**：`Async Function` 不允许 `ByRef` 形参（Fact，与 C# 一致），因此本特性天然不进入 Async 函数体。`Async Sub` 同理。无新交互。
- **ByRef Returns（2016-05-06 主线）**：函数返回存储位置的能力与本建议正交——`Return ByRefExpr, value:=x` 里，被返回的引用先求值、再执行写回，顺序由 (i) 覆盖。不冲突。
- **Lambda**：多行 lambda 内的 `Return` 只能写回 lambda 自身的形参；`Return True, value:=result` 若试图写回**外层方法**的 ByRef 形参——lambda 不能捕获并按引用写外层形参，编译器报错（与今天 `Sub() : outerParam = 1 : End Sub` 的捕获规则一致）。无新语义。
- **元组（2016）**：见第 1 条括号分歧。这是本特性与既有语法唯一实质的交互点。
- **Late binding / Option Strict Off**：宽松模式下 `Return True, value:=result` 的赋值项按普通赋值转换规则（窄化允许）；严格模式窄化报错。两条路径行为必须一致——见第 6 条。

#### 5. Breaking change 与兼容性

**零破坏（Fact）。** `Return True, value:=result` 与 `Return value:=result` 在今天都是语法错误——VB 没有逗号运算符，裸逗号列表不是合法表达式，元组字面量必须带括号。本特性是**纯语法增量**：不会改变任何既有代码的解析、绑定或运行行为。这一点强于绝大多数候选特性，是它最大的资产。

#### 6. Option Strict / 编译选项分叉

严格 / 宽松两路径一致：赋值项与普通赋值共用转换规则，只受 `Option Strict` 的窄化门槛影响，与返回表达式无关。`Probably`：宽松路径下 `Return True, value:=someWideningOnly` 的行为与 `value = someWideningOnly` 完全一致——没有第二条转换规则。

#### 7. IDE / IntelliSense

`Return True, ` 之后 IntelliSense 应补全 `ByRef`/`Out` 形参名，直接复用命名实参的补全与 `:=` 插入机制——这是现成的（2014 年主线就讨论过 pretty-lister 是否该在调用点插入 `Out`，2017-08-23 又讨论了 OutAttribute 的识别）。Return 不是调用点，签名帮助不适用；但**括号分歧**（元组 vs 写回）要求 IDE 对 `Return (True, root:=1)` 与 `Return True, root:=1` 做不同的着色与 InfoTip，否则读者看不出哪条是元组、哪条是写回。InfoTip 可考虑在写回项上标"写回：参数 value"。

#### 8. 数据 / 普遍性

`Try*` 模式无处不在，但每次压缩**只省一行**，且两个写法（`value = result : Return True`）都清楚。`Suspect`：这是"低密度、高频率"的改善——每个真实项目会碰到几百次，但单次收益微小；真正的普遍性证据来自 out-arguments 建议（TryGetValue 的隐式声明 + 空引用警告治理），本建议是它的配角。没有量化数据支撑"Return 带写回"本身的独立需求。

#### 9. 更简替代

- **现状**：`value = result` + `Return True`。两行、清楚、零学习成本。省一行的说服力有限——这也是为什么我们不肯单独为省一行做一个特性。
- **元组**：改签名、调用方解构，`Try*` 形态不适用。
- **局部函数 / 辅助方法**：把"计算 + 写回"包进私有 helper，只缓解调用方侧，不解决函数内部"交付"的耦合。
- **Out 参数建议自带**：那边已经写了 `Return True, root:=1`，说明 Out 场景**必须**有 Return 赋值语法才能成立——这反过来证明本建议不是可有可无的糖，而是 Out 参数建议的结构件。

#### 10. 复杂度 / 成本 / 优先级

实现面小：`Return` 文法扩展（一条产生式）+ binder（赋值项绑定到形参）+ definite-assignment（Out 未赋值警告）+ 代码生成（`ret` 前插入赋值序列，copy-out 复用既有机制）。**独立价值中低**——省一行样板；**作为 out-arguments 结构件价值高**——没有它那边不成立。优先级随 out-arguments（组 9）走；`Sub` 写回切片可先行。

#### 11. 运行时 / CLR 硬约束

无。赋值在方法退出前完成，是普通 `stloc`/`stind` 序列；ByRef copy-out 是 VB 既有发射行为。不触达 CLR 存储规则、不破坏 PEVerify、无表达式树问题（表达式树不包含 Return 语句）。`Return True, value:=result` 在 IL 层就是"若干 store + 一条 `ret`"。

#### 12. 值不值得做

价值（承载 out-arguments、Sub 写回、消 `return-without-assign` 警告）中；成本（文法 + binder + 代码生成小增量）低；风险（零 break、纯增量、显式语法）低。**值得做——但作为 out-arguments 的组成部分，不独立立项。** 独立立项的诱惑只是"省一行"，那不够；挂在 Out 参数上，它从"糖"变成"结构"。

### VB 基因对照

- **永不破坏（原则 #1）**：零 break，纯增量，满分——这是全场最强论据。
- **保持 VB-like（原则 #2）**：`name:=expr` 复用命名实参，极 VB。但"Return 夹带赋值"是否"看起来像 VB"有争议——2014 年主线对更宽的 Out 声明表达式说过 "None of this feels naturally 'VB'ish. We're happy if VB sticks merely to Out parameters"。Return 赋值比 Out 声明表达式收敛得多，但仍要面对"Return 应该只返回"的直觉。
- **不引入"第二种做事方式"（原则 #3）**：**最大扣分项**。`value = result : Return True` 已存在且清楚，`Return True, value:=result` 是第二种方式。反方论证：Out 参数建议已经承诺了 `Return True, root:=1`，本建议不是新增第二种方式，而是为 out-arguments 这个"第一种方式"补齐语法。若没有 out-arguments，这个特性不值得独立存在。
- **读起来像英语、对新手友好（原则 #5）**：`Return True, value:=result` 可读性尚可，但比显式两行**不透明**——新手可能忽略写回。C 片（`Return status:=-1, message:="not ready"`）反而最顺口："提前退出并带上这两个输出"。
- **避免隐蔽语义变化（原则 #7）**：`name:=` 显式，救了一半；括号分歧（元组 vs 写回）踩了 `Return?` 的同族雷，靠文法解析 + IDE 区分对冲。
- **消除常见样板（原则 #9）**：只省一行，样板消除效果弱——这条被夸大了，正文里我们不以此为卖点。
- **与主线关系（对照表 2.3）**：Out 参数是主线"讨论中"→"并入模式匹配"的领域（2014 #42 Approved in principle → 2017 "Out parameter separately" → 2018 #305 "LDM Reviewed: No Plans"，Out 变量"covered better in #60"、模式匹配 "#124"）。本建议是 Anthony 对 Out 参数完整化设计（out-arguments）的语法子集——**主线一致方向上的独立延伸**，且与主线的 Out 变量 / 模式匹配共享直觉：把"取数 + 判定"绑成一次交付。

### RESOLUTION:

1. **原则上接受**"`Return` 语句可携带对 `ByRef`/`Out` 形参的赋值"，但**不作为独立特性**：它是 `proposal-out-arguments.md` 的语法组成部分，与该建议同一份 spec、同一原型（呼应 2017-08-23 "Out parameter separately" 与 2018 #305 把 Out 并入模式匹配/No Plans 的边界划分——我们认领 Out 参数设计，但 Return 赋值不独立漂浮）。
2. **语法形态 = PROPOSAL A 的具名形式** `Return <表达式> [, name:=<表达式>]...`，复用命名实参 `:=`。**位置形式暂缓（Table）**——避免"第几个形参"的隐式匹配与 lvalue 判定扩散。
3. **v1 规则 = PROPOSAL B 的纪律**：赋值目标只允许 `ByRef` 与 `Out` 形参；`ByVal` 目标报错。`Optional ByRef` 未提及则保持原值；含 `Out` 形参的方法每条 `Return` 必须对全部 `Out` 赋值，否则编译警告（承接 2014 #42 的 `return-without-assign`）。
4. **求值顺序 = 语义 (i)**：先求返回表达式，再从左到右执行赋值项；语义等价于 `Dim tmp = expr : name = x : Return tmp`，copy-in/copy-out 由既有机制保证。
5. **PROPOSAL C（Sub 写回）是第一个独立切片**：`Return name:=expr` 对 `Sub`/`Async Sub` 开放，`Return` 在 Sub 里本就允许裸用，零文法冲突，价值密度最高。
6. **括号分歧写进 spec**：`Return (True, root:=1)` 是具名元组字面量，`Return True, root:=1` 是本特性；文法以括号切分，IDE 分别着色，作为已知歧义点监视。
7. **`Exit Function` / `Exit Sub` 不携带赋值（Table）**；`Async Function` 天然排除（无 `ByRef` 形参）；late binding 与元组交互按第 4、6 条处理。
8. **不加 `langversion` 门控**：零 break 的纯增量不需要门控（与收窄类特性不同）。

### Implication:

- 将本建议并入 `proposal-out-arguments.md` 的 Detailed design，补写：Return 赋值文法（BNF）、求值顺序 (i)、copy-in/copy-out 说明、ByVal 目标错误、`Optional Out` 规则、Sub 写回切片、Option Strict 分叉、括号分歧。
- 最小原型按 C 片先行：`Sub` 写回 + 普通 `ByRef` 写回，验证 binder、definite-assignment 警告、代码生成（`ret` 前赋值序列）与 IDE 补全。
- 与 Tuples 团队对表：确认括号分歧的解析规则，评估是否需要在 IDE 里区分"元组 Return"与"写回 Return"。
- 与组 9（out-arguments）协调：两份建议的未决问题（`Optional Out`、失败分支 `value` 可见性）合并为一套。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：求值顺序语义 (i) 是否在 spec 层面正式定案；若社区反馈认为"赋值应先于返回表达式"（语义 ii），等价改写会改变结果——需要明确的风险声明。
- `OPEN QUESTIONS`：`Optional Out` 与"每条 Return 必须赋值"规则的调和方式（禁止 vs 默认值）。
- `OPEN QUESTIONS`：位置形式 `Return True, a` 是否有真实需求值得解除 Table；若 out-arguments 的隐式声明变量走位置形式会更短，但绑定复杂。
- `TODO`：量化"返回 + 写回"模式在真实代码中的占比，为效果维度补证据。
- `Follow-up`：与 `proposal-intersection-union-types.md` 的 Out 结合（模式方法提取值时是否可能给出交/并类型目标）。
- `Follow-up`：2017-08-23 提到的 "C# -> VB -> C# 与 C# -> VB -> VB" 互操作矩阵——`Return` 写回是否影响对 C# `out` 参数调用的互操作展示。

### 状态

- **LDM 状态：Active（作为 out-arguments 的组成部分）**；standalone 状态为 **Table**——我们不独立做"Return 带赋值"，但它是 Out 参数建议不可少的承载语法。
- **三态判定：Active（限定范围）** — 零 break、显式语法、与 out-arguments 咬合；Sub 写回切片先行，其余随 Out 参数建议落地。

---

## 附录：特性评价

# 建议评价报告：proposal-return-byref.md

## 评价对象

- 建议：proposal-return-byref.md — `Return` 赋值给 `ByRef`/`Out` 参数
- 来源：Anthony 原文 §3.13 `Return`（`Return True, value:=result`，`..\AnthonyDesign_wordpress.txt` L1111–1116）；§8 "General Modernization and Evolution II (Declarations)" 的 `IsPerfectSquare` 案例（`Return True, root:=1`，L1426–1439）
- 配方目标：`Return` 在返回函数值的同时一并写回 `ByRef`/`Out` 输出参数，一条语句完成"返回结果 + 写回输出"

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失"。Motivation 清晰（`Try*` 场景）、示例可操作且能编译；但每次压缩只省一行，效果弱；Sub 写回（价值最高切片）建议未强调；无原型 | 已检查 | 无原型封顶；"返回 + 写回"占比无数据；主效果（省样板）被夸大，真正的效果价值在支撑 out-arguments |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。复用命名实参 `:=` 是 VB 基因；但"Return 夹带赋值"与原则 #3（不引入第二种做事方式）有张力；括号分歧与元组语法冲突 | 已检查 | 语义清洁性（Return 只表达返回）未讨论；与 2016 Tuples 的括号边界是已知冲突，建议未识别 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、示例与 Anthony 原文逐字一致、3 个未决问题诚实（1–3 健康区间） | 已检查 | Detailed design 极薄（仅一个示例）：未覆盖求值顺序、copy-in/copy-out、ByVal 目标错误、Option Strict 分叉、元组括号分歧、Async 排除；Alternatives 提到元组但未认真考虑"什么都不做"；状态行占位链接 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。光（盘活 `Try*` 惯用法）+ 雷（与 out-arguments 协同）正向；风（Return 语义边界、元组括号边界）有歧义风险未权衡 | 已检查（预测待定） | 与 Return 核心语义的边界、与元组的语法边界均未在文档中权衡；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。材料 = Anthony §3.13 + §8（原创）；未声明"继承 VB 命名实参 `:=` 惯用法"；未声明与主线 Out 参数讨论（#42/#305/#60/#124）的同源关系 | 已检查 | 原创身份明确但血缘标注缺失；`IsPerfectSquare` 案例引用了 `Let` 与 `Out` 形参（属 out-arguments 建议），成分边界与另一建议重叠未说明；无杂质 |

## 设计原则对照

- **与 VB 基因：基本一致但有张力**——复用命名实参 `:=`（一致）；零 break（原则 #1 满分）；但"Return 带副作用"与原则 #3（第二种做事方式）、原则 #5（不透明）张力；括号分歧与原则 #7（隐蔽语义变化）同族，靠显式 `name:=` + 文法切分对冲。
- **与主线关系：主线一致方向上的 Anthony 独立延伸**——Out 参数是主线长期"讨论中"→"并入模式匹配/No Plans"的领域（2014 #42 Approved in principle → 2017 "Out parameter separately" → 2018 #305 Out 变量 "covered better in #60"、模式匹配 "#124"）。本建议是 Anthony 的 out-arguments 完整设计的承载语法，与主线 Out 变量/模式匹配共享"取数 + 判定一次交付"的直觉。与 `proposal-out-arguments.md` 强耦合；与 2016 Tuples 在括号边界交互。
- **破坏性变更：无**——`Return` 后跟裸逗号列表今天必是语法错误，纯增量。

## 总评

- **达成程度：部分达成**——概念成立、零 break、与 out-arguments 咬合是实打实的资产；但独立价值弱、文档过薄、元组括号分歧未解决、求值顺序未定义。
- **LDM 三态建议：Active（作为 out-arguments 的组成部分）**；standalone 为 **Table**。Sub 写回切片（PROPOSAL C）优先原型。
- **主要问题**：① Return 夹带赋值与"Return 只表达返回"的语义清洁性；② 与元组字面量的括号分歧（`Return (a, b)` vs `Return a, b:=x`）未在文档中识别；③ 独立价值弱——省一行样板不足以立项，必须挂在 out-arguments 上；④ 求值顺序 / copy-in-copy-out / Option Strict 分叉未写进 spec。

## 返工建议

- **补充章节**：Detailed design 扩写——Return 赋值文法（BNF：`Return` = [表达式] {, name:=表达式}）、求值顺序语义 (i)、copy-in/copy-out 说明、ByVal 目标错误规则、`Optional Out` 规则、Sub 写回切片、Option Strict 分叉、元组括号分歧、Async 排除。
- **补充证据**：最小原型（Sub 写回优先）；"返回 + 写回"模式在真实代码中的占比数据；IDE 补全与元组/写回区分着色的验证。
- **未决问题处理**：具名/位置（v1 具名，位置 Table）；与 out-arguments 统一（同一份 spec、合并 `Optional Out` 与失败分支可见性）；Sub/Async Sub 场景（v1 优先，作为第一切片）。
- **设计探索**：`Exit Function` / `Exit Sub` 是否携带赋值（Table，一致性诱人但面翻倍）；与 2016 ByRef Returns 的求值顺序交互；与 `proposal-intersection-union-types.md` 的 Out 结合；与主线 #60/#124（Out 变量、模式匹配）的对表，确认 VBScript.NET 的 Out 参数设计与主线模式匹配路径不冲突。

---

## 附录：C# 生态与互操作考量

> 本附录依据 `..\..\csharplang-index.md`（dotnet/csharplang 官方仓库 interop 浓缩索引，主题 T2/M3）并在 `..\..\csharplang` 中逐条核实。本提案（`Return` 给 `ByRef`/`Out` 形参赋值）与 C# interop 的关系是**强相关**：它落在 C# 自 7.0 起持续投入的 byref/ref 主线上（决策文件 M3：`return-byref / out-arguments ↔ ref 系`）。核心张力不在语法——C# 没有"返回 + 写回"的单语句对应物——而在**语义模型**：VB `ByRef` 的 copy-in/copy-out ≠ C# `ref` 的直接别名，且 VB 不参与 C# 的 ref-safe-context（决策文件 M3 明示：跨语言调用时 `RefSafetyRules` 只对 C# 模块生效）。C# 原文逐字引用均标注来源文件；VB 侧元数据发射行为（`ByRef` 是否带 `[In]`/`[Out]`）在 csharplang 无正文，按既有 CLR 元数据行为陈述并列入 OPEN QUESTIONS。

### 相关 C# 现实方向

本提案主题（方法返回时按引用写回输出）在 C#/CLR/.NET 生态的对应走向：

1. **C# 7.0 起把 byref 做成一等语言能力，但"返回 + 写回"始终是两条语句。** C# 7.0 的 ref locals/ref returns 规范在本仓库只是占位：**"In C# 7.0 we added support for *ref locals and ref returns*. This is a placeholder for its specification."** → `proposals\csharp-7.0\ref-locals-returns.md`。同期 C# 7.0 给调用点引入 `out var`：**"The *out variable declaration* feature enables a variable to be declared at the location that it is being passed as an `out` argument."** → `proposals\csharp-7.0\out-var.md`。也就是说，C# 把"取输出"的糖放在**调用点**（`out var` 就地声明），函数**体内**仍是先赋值 `value = ...; return ...` 两行——本提案（`Return True, value:=result`）压掉的正是 C# 至今没有去压的那一行。C# 从未提案过"return 同时写回 out/ref 形参"的单语句语法（本库 grep 无对应 proposal）。

2. **C# 7.2 确立"ref-like 类型只存活于栈"的安全基线**，这是 ref-safe-context 的源头：**"The main reason for the additional safety rules when dealing with types like `Span<T>` and `ReadOnlySpan<T>` is that such types must be confined to the execution stack."** → `proposals\csharp-7.2\span-safety.md`（Introduction）。同一版本还引入 `in` 参数（`proposals\csharp-7.2\readonly-ref.md`）。索引 T2 概括这条主线：C# 持续把"接近指针"的能力做成**不放弃类型/内存安全**的语言特性。

3. **C# 11 把 ref 安全模型升级并放进元数据（`RefSafetyRules`），且 `out` 参数被隐式收窄。** → `proposals\csharp-11.0\low-level-struct-improvements.md`：
   - 动机点名 ref returns 是既有低层资产：**"Earlier versions of C# added a number of low level performance features to the language: `ref` returns, `ref struct`, function pointers, etc."**（Motivation）。
   - 新增 `ref` fields + `scoped` + `[UnscopedRef]`；对 `out` 的默认改为：**"the language will change the default *ref-safe-context* value for `out` parameters to be *function-member*. Effectively `out` parameters are implicitly `scoped out` going forward."**（Change the behavior of `out` parameters）；并把 out 定性为纯输出：**"An argument to an `out` parameter does not contribute to the return, it is simply an output."**。
   - 模块级版本标记：**"the compiler will emit a new `[module: RefSafetyRules(11)]` attribute when the module is compiled with `-langversion:11` or higher or compiled with a corlib containing the feature flag for `ref` fields."**；**"Essentially, when analyzing a call to a method compiled with an older compiler, the C#11 compiler will use C#7.2 ref safe context rules."**；**"A pre-C#11 compiler will ignore any `RefSafetyRulesAttribute` and analyze method calls with C#7.2 rules only."**（RefSafetyRulesAttribute 节）。

4. **C# 12 加 `ref readonly` 形参（声明处修饰符）**，并把 `in` 的宽松调用点规则写进文档：**"`in` parameters allow both lvalues and rvalues and can be used without any annotation at the callsite."** → `proposals\csharp-12.0\ref-readonly-parameters.md`（Motivation）。方向是让"捕获/返回引用"的 API 在调用点被显式标注（`ref readonly` 对裸调用点告警）。

5. **C# 13 允许 `ref struct` 实现接口**，但确认 ref-like 类型长期被隔离在 .NET 抽象之外：**"The inability for `ref struct` to implement interfaces means they cannot participate in fairly fundamental abstraction techniques of .NET."** → `proposals\csharp-13.0\ref-struct-interfaces.md`（Motivation）。这条对 VB 的意义在于：现代 BCL 高吞吐 API 正迁往 `Span<T>`/`ref struct`，而 VB 侧（含 .vbx 现状）**不支持 ref struct**，消费面结构性收窄。

6. **unsafe-evolution 对 VB 的明确表态**：**"We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either."** → `proposals\unsafe-evolution.md`（VB 小节）。即 C# 侧认为 VB 不需要加入 unsafe/requires-unsafe 体系——但 .vbx 仍需**识别**这些新元数据（`RequiresUnsafeAttribute`/`MemorySafetyRulesAttribute`），否则无法校验跨语言调用（决策文件 M8）。

### 现实 vs 提案

| 本提案主题（判定） | C# 现实 | 关系 | 理由 |
|---|---|---|---|
| `Return` 给 `ByRef`/`Out` 写回（Active） | C# 无"return + 写回"单语句；`out var` 在调用点、体内仍是两行 | **兼容，VB 特色** | 方向同向（C# 也在强化 byref/ref 返回，T2/M3），但语法无对应物——这是纯 VB 方言增量；不产生新元数据，IL 仍是"若干 store + 一条 `ret`"，与 C# 互操作零冲突 |
| 赋值目标纪律 = 只准 `ByRef`/`Out`（RESOLUTION 3） | C# `out` 要求返回前 definite assignment；C# 11 后 out 是隐式 `scoped` 的纯输出 | **兼容，且互相印证** | VB"每条 `Return` 必须对所有 `Out` 赋值"与 C#"out 返回前必须定值"是同一契约的两侧；C# 原文 "it is simply an output" 为 RESOLUTION 3 提供 C# 旁证 |
| `ByRef` 的 copy-in/copy-out（正文机制） | C# `ref` 是直接别名、无 copy-back；`in`/`ref readonly` 只读 | **需桥接（语义模型差异）** | 元数据层面 VB `ByRef` 与 C# `ref` 同为 `ELEMENT_TYPE_BYREF`；但 VB 对非 lvalue 实参/property 有 copy-back，C# 没有。跨语言时 C# 把 VB `ByRef` 当 `ref` 看，VB 侧 copy-back 由调用边界既有机制承担，`Return` 写回发生在其内、不改边界——安全，但语义契约不同 |
| ref-safe-context 参与（正文未讨论） | C# 11 起用 `RefSafetyRules` 标记模块规则版本 | **需桥接（VB 不参与）** | VB 模块无 `[module: RefSafetyRules(11)]` → C# 11+ 按 7.2 规则分析对 VB 模块的调用，即不假设 VB `ByRef` 形参可逃逸为 ref field——对 .vbx 反而是"不被高估逃逸"的安全默认；但 .vbx 若要正确校验"调用 C# 模块"，必须识别该属性及其未来版本 |
| `out`/`ref readonly`/`in` 消费（正文未讨论） | C# 12 `ref readonly` 形参、BCL 大量 `in`/`ref struct` API | **需桥接（元数据识别）** | .vbx 若把 C# `in`/`ref readonly` 形参当普通 ByRef，可对只读引用写回——违反 C# 契约。需识别 `[In]`/`IsReadOnlyAttribute` 并把写回降级为错误；`Span<T>`/`ref struct` 参数则因 VB 无 ref struct 而无法消费（结构断层，非语法问题） |
| Async 天然排除（RESOLUTION 7） | C# async 方法同样禁止 `ref`/`out` 形参（与 VB 一致） | **与 C# 一致** | 无新交互；两语言在此同构 |
| 不加 langversion 门控（RESOLUTION 8） | C# 11 把 ref fields/scoped 门控在 `-langversion:11` + runtime feature flag 之后 | **兼容，VB 特立** | .vbx 的 `Return` 写回是纯语法增量（零 break），无新元数据，无需门控；C# 门控的是**新元数据/新规则**，与本特性无关。跨语言时 .vbx 不因 C# 的门控而被迫改语法 |

结论性观察：**本提案与 C# 生态无"冲突"，也无"脱节"**——它落在 C# 持续投入的 byref 主线上，但走的是 C# 没有走的"函数体内一条语句交付"方向，属 VB 特色自主空间；真正需要动手的是**桥接层**：元数据识别（`RefSafetyRules`/`ScopedRef`/`UnscopedRef`/`[In]`/`IsReadOnlyAttribute`）与语义契约对齐（VB copy-in/copy-out vs C# 直接别名/只读引用）。

### 对 VBScript.NET 的适应建议

1. **默认安全：保持 copy-in/copy-out 为 `ByRef` 的唯一语义，`Return` 写回不改调用边界。** 正文已定 `Return True, value:=result` 等价于 `value = result` 后 `Return True`，写回发生在方法退出前、copy-out 由既有机制承担。.vbx 应坚持这一"写回在边界内完成"的模型，**不要**引入 C# 式直接别名（`ref` 无 copy-back）作为 `ByRef` 的第二种语义——那会同时引入 ref-safe-context 义务，超出脚本层的安全承诺（呼应决策文件 M3：VB 不参与 ref-safe-context）。
2. **元数据对齐（`Out` 形参标 `[Out]`）**：.vbx 编译 out-arguments 建议的 `Out` 形参时，应在 `&T` 上发射 `OutAttribute`，与 C# `out` 的元数据形态一致；这样 `Return True, root:=1` 写回的 `Function` 在 C# 调用方眼中就是标准的 `bool Try(..., out int root)`，C# 的 definite-assignment 契约直接满足——这是 2017-08-23 follow-up"互操作矩阵"的正向兑现。
3. **识别新元数据（按决策文件 M8 扩展）**：.vbx 编译器需识别：`RefSafetyRulesAttribute`（模块，判断 C# 侧用哪套 ref 规则）、`ScopedRefAttribute`（`scoped` 形参 = 不逃逸）、`UnscopedRefAttribute`（`[UnscopedRef] out` 恢复逃逸）、`[In]` + `IsReadOnlyAttribute`（`in`/`ref readonly`，禁止写回）、`RequiresUnsafeAttribute`/`MemorySafetyRulesAttribute`（unsafe-evolution）。这是"正确消费 C# 模块"的前提，不是可选优化。
4. **source-gen 桥（对 ref struct/Span 断层）**：现代 BCL 高吞吐 API 大量是 `ref struct`/`Span<T>`，VB 无法直接消费（上述第 5 条 C# 现实）。建议对"脚本需要调用的高性能 C# API"用 source generator 生成 **ByRef/copy-in-copy-out 外观包装**（T6 方向：编译期生成替代运行时动态），把 `Span<T>` 参数面暴露成 .vbx 可写的数组/ByRef 面，保持脚本侧语法不变，同时保住 AOT/trimming 友好。
5. **按需动态仅留给 COM/Office 晚期绑定**：`Return` 写回属静态类型化通道，应落在"默认安全"侧；动态 `Any`/晚期绑定（决策文件 M2）才走运行时反射。不要把"返回 + 写回"与动态调用混为一谈——静态路径在 AOT 下零障碍，动态路径是脚本层的显式 opt-in。

### 对既有 RESOLUTION / 三态判定的影响

**无推翻性影响**；C# 证据只增不减，Active（限定范围）判定原样成立。

- **强化 RESOLUTION 3（`Out` 必须全部赋值）**——C# 侧 `out` 就是"返回前必须定值 + 纯输出"（`it is simply an output`），VB 的"每条 `Return` 对全部 `Out` 赋值否则警告"与 C# 契约同向；跨语言时这甚至是 C# definite-assignment 的**直接满足手段**。
- **为正文"零 break / 纯增量"提供元数据佐证**——`Return` 写回发射的是普通 store + `ret`，形参仍是 `&T`（BYREF），不引入 `RefSafetyRules` 等任何新模块/形参属性；C# 调用方对 .vbx 模块按 7.2 规则分析，不会因本特性而改变对 VB `ByRef` 形参的逃逸假设。
- **给 follow-up 的"C# → VB → C# / C# → VB → VB 互操作矩阵"补上具体机制**——当 VB `Function` 实现了带 C# `out` 形参的接口/基类方法，`Return True, root:=1` / `Return False, root:=Nothing` 在单语句内保证 out 返回前定值；位置形式（Table）若未来启用，跨语言时"按形参顺序匹配"的绑定须在矩阵中验证与 C# `ref`/`out` 的对应。
- **新增一条实现注意（不推翻，需并入原型）**——RESOLUTION 6 的括号分歧（元组 vs 写回）是 VB 内部分歧，与 C# 无关；但 C# 12 `ref readonly` 形参出现在 BCL 时，.vbx 对 `in`/`ref readonly` 的写回必须报错（见适应建议 3），这一规则应在 out-arguments 同一份 spec 里写明，避免"把 C# 只读引用当可写 ByRef"。

### 引用清单（本附录引用的 C# 原文，逐字）

- 「In C# 7.0 we added support for *ref locals and ref returns*. This is a placeholder for its specification.」→ `proposals\csharp-7.0\ref-locals-returns.md`
- 「The *out variable declaration* feature enables a variable to be declared at the location that it is being passed as an `out` argument.」→ `proposals\csharp-7.0\out-var.md`
- 「The main reason for the additional safety rules when dealing with types like `Span<T>` and `ReadOnlySpan<T>` is that such types must be confined to the execution stack.」→ `proposals\csharp-7.2\span-safety.md`（Introduction）
- 「Earlier versions of C# added a number of low level performance features to the language: `ref` returns, `ref struct`, function pointers, etc.」→ `proposals\csharp-11.0\low-level-struct-improvements.md`（Motivation）
- 「the language will change the default *ref-safe-context* value for `out` parameters to be *function-member*. Effectively `out` parameters are implicitly `scoped out` going forward.」→ `proposals\csharp-11.0\low-level-struct-improvements.md`（Change the behavior of `out` parameters）
- 「An argument to an `out` parameter does not contribute to the return, it is simply an output.」→ `proposals\csharp-11.0\low-level-struct-improvements.md`（Change the behavior of `out` parameters）
- 「the compiler will emit a new `[module: RefSafetyRules(11)]` attribute when the module is compiled with `-langversion:11` or higher or compiled with a corlib containing the feature flag for `ref` fields.」→ `proposals\csharp-11.0\low-level-struct-improvements.md`（RefSafetyRulesAttribute）
- 「Essentially, when analyzing a call to a method compiled with an older compiler, the C#11 compiler will use C#7.2 ref safe context rules.」→ `proposals\csharp-11.0\low-level-struct-improvements.md`（RefSafetyRulesAttribute）
- 「A pre-C#11 compiler will ignore any `RefSafetyRulesAttribute` and analyze method calls with C#7.2 rules only.」→ `proposals\csharp-11.0\low-level-struct-improvements.md`（RefSafetyRulesAttribute）
- 「`in` parameters allow both lvalues and rvalues and can be used without any annotation at the callsite.」→ `proposals\csharp-12.0\ref-readonly-parameters.md`（Motivation）
- 「The inability for `ref struct` to implement interfaces means they cannot participate in fairly fundamental abstraction techniques of .NET.」→ `proposals\csharp-13.0\ref-struct-interfaces.md`（Motivation）
- 「We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either.」→ `proposals\unsafe-evolution.md`（VB 小节）

### OPEN QUESTIONS / Suspect

- **Suspect（VB 侧元数据，非 csharplang 正文）**：VB 编译器对 `ByRef` 形参发射的元数据是否完全不带 `[In]`/`[Out]`（即与 C# `ref` 同形、与 `out` 仅差 `[Out]`）——这是 CLR/ECMA-335 元数据与 Roslyn VB 发射器的既有行为，csharplang 仓库无 VB 发射文档，本附录按既定行为陈述；如需逐字依据，须查 dotnet/vblang 或 Roslyn VB 发射器源码。
- **OPEN QUESTION**：.vbx 对 C# `in`/`ref readonly` 形参的写回应**报错**还是降级为 ByRef？是否引入 `ByRef ReadOnly`/`In` 修饰符？建议先报错（保守、满足 C# 契约），语法扩展留给后续。
- **OPEN QUESTION**：未来 C# 若发射 `RefSafetyRules(12+)`，.vbx 应如何声明自己模块的 ref 规则版本（VB 模块当前无该属性 = C# 按 7.2 处理）？这是长期互操作协议问题，非本提案范围，但 .vbx 的"识别新元数据"清单应含版本协商。
- **OPEN QUESTION**：位置形式 `Return True, a` 若解除 Table，在跨语言场景（C# `ref`/`out` 实参）下"按形参顺序匹配"与 C# 调用点修饰符（`ref`/`in`/`out`）的对应关系需在互操作矩阵中验证——本附录暂不预判。
- **关系弱申明（部分）**：本提案与 T3 指针/函数指针、T4 COM、T5 AOT、T6 source-gen 无直接冲突；与 AOT 的唯一接触点是"静态路径零障碍、动态路径 opt-in"（适应建议 5）。真正接触面集中在 T2 ref 系（ref returns / ref fields / ref-safe-context / RefSafetyRules）与元数据识别。
