# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。今天的议题来自建议目录的 **inactive** 子目录——`proposal-native-instruction-helpers.md`（Anthony 原文第 18 章 18.23 "Platform Native Instruction Helpers"）。这份建议没有一句示例代码，也没有任何文法；它记录的是一个**机制方向**：把"编译器认识某些方法调用并降级为原生 IL 指令"的既有做法**泛化**——扩展内联列表、并在运行时定义一个"形状正确"的 well-known 模块，让更多 IL 不经新增关键字就可用 VB 表达。建议同时明确拒绝了两条更激进的路：IL Literals（在源码里写 IL）与"为每个 opcode 加一个一次性关键字"。

所以本场会议真正的问题是：**这个机制方向值得激活、继续保持搁置，还是应该被拆解成它真正指向的那些设计线程？** 我们开场就预期：这不会是一次"通过某份提案"的会议，而是一次"确认我们该不该为它发明一个特性"的会议——但和上一场 json-serializers 不同，这份建议背后有一个**真实存在的机制先例链**（DirectCast/TryCast 映射 unbox/isinst、2014 年 `conv.i4` 优化决议、2018 年 Issue #86），所以辩论必须落在"先例边界在哪、泛化到哪一步会踩线"上。

## Agenda

* [Proposal: 平台原生指令辅助（Platform Native Instruction Helpers）](#proposal-平台原生指令辅助)

## Proposal: 平台原生指令辅助（Platform Native Instruction Helpers）

_Related: [vblang #86 – Optimize conversions from calls to Int/Fix functions and System.Math methods known to return whole Single/Double values to native opcodes](https://github.com/dotnet/vblang/issues/86)；[vblang #211 – usage of any installed programming language](https://github.com/dotnet/vblang/issues/211)；[vblang #167 – Support for Return? construct](https://github.com/dotnet/vblang/issues/167)；ModVB：`proposal-runtime-library.md`、`proposal-case-insensitivity.md`、`proposal-name-resolution.md`、`proposal-postfix-casting.md`_

### 场景与缺口

We started from the proposal's own opening, which把请求方与回应方的双重困境摆了出来：

> Tired of being asked to add a one-off keyword for every opcode in the CLR. I wouldn't go so far as to add "IL Literals" but maybe there's something that gets the job done without directly bloating the language (similar to P/Invoke).（Anthony 18.23，逐字引用）

缺口是真实的：今天确实有一些 CLR 能力在 VB 里表达不了，而"为每个 opcode 加关键字"与"直接写 IL"又都是不可接受的答案。但把建议列出的四个场景摊开看，我们立刻发现**这不是一个缺口，而是四个性质不同的缺口**：

```vb
' 今天：这四种能力在 VB 里表达不了。
' ① 大小写敏感成员解析 —— 对"写得很烂的库"。
'    库（C# 作者）里同时存在 value 与 Value 两个成员，VB 无法区分：
Dim w As New PoorlyWritten.Widget()
Dim a = w.value     ' VB 大小写不敏感 ⇒ 编译器要么报歧义、要么任意绑定一个

' ② Checked 算术 —— 默认回绕，无 OverflowException：
Dim n As Integer = Integer.MaxValue
Dim m = n + 1       ' m = Integer.MinValue（回绕），不抛异常

' ③ Volatile —— 方法形态存在，字段修饰符不存在：
Dim flag = Threading.Volatile.Read(Me._flag)
' VB 没有 volatile 字段修饰符（C# 有）。

' ④ Localloc —— 栈上分配，今天 VB 无任何等价写法。
```

①不是"指令"问题，是**绑定规则**问题——它跟 IL opcode 无关；②④才是真正的"指令不可达"问题；③一半是方法形态（`Threading.Volatile` 已覆盖）、一半是声明面问题（volatile **字段修饰符**，方法调用形态根本表达不了）。**We think：把四件事捆成一件，是这个机制方向的第一个也是最大的问题——后面所有争论几乎都由此而来。**

但我们也要诚实记录机制的真实性。VB 语言规范白纸黑字承认过"为暴露主流指令而加语法"的先例，[spec/expressions.md](https://github.com/dotnet/vblang/blob/main/spec/expressions.md#cast-expressions) 写道：

> The purpose of `DirectCast` is to provide the functionality of the "unbox" instruction, while the purpose of `TryCast` is to provide the functionality of the "isinst" instruction. Since they map onto CLR instructions, supporting conversions not directly supported by the CLR would defeat the intended purpose.（逐字引用）

而"编译器认识方法调用并降级为原生指令"也不是新想法。2014 年 3 月 12 日的 LDM 就决议过：`CInt(Math.Truncate(d))` 应发射 `conv.i4` 或 `conv.ovf.i4`（取决于项目级 check-overflow 设置），并把这条线明确界定为：

> This is a compiler optimization, pure and simple. It adds no new syntax or concepts or library functions. We identify targeted scenarios and generate optimal IL in those cases where semantics would not be affected. This seemed the cleanest approach. Specifically, it seemed better than adding any of the following syntaxes: `DirectCast(d, Integer)`; `(Integer)d`; `VB.FastCast(d)`; `d As Integer`（逐字引用，LDM-2014-03-12）

2018 年 3 月 21 日会议在 Issue #86 上延续了同一条线（Anthony 本人以嘉宾出席），并给出了两句对今天至关重要的话：

> Treat it as an optimization, not a feature to keep it narrow and get it done.（逐字引用）

> Probably some people that use CInt() probably don't care and do want optimization. But this is a breaking change and can't be done.（逐字引用）

**这两句就是本次讨论的地基。** 先例告诉我们：把既有调用优化成指令，是"优化"——前提是语义完全不受影响、并且只做"把已知数学函数结果直接转换"这类窄场景；而一旦优化会改变行为（#86 里有人希望 CInt 变快但那是 breaking change），就"can't be done"。本建议的机制却迈出了一大步：**它定义的不是"优化既有调用"，而是"发明新的 well-known 方法，让调用它们的源码被悄悄替换成指令"。** 这一步从"优化"跨进了"新的语言表面"——这正是我们要审的。

### 候选方案

**PROPOSAL A — Well-known 魔法模块（建议原文的机制）。** 扩展编译器的内联列表；在运行时定义一个 well-known 模块，提供"形状正确"的共享方法；任何**精确形状的早期绑定调用**被降级为对应 IL 指令；形状不符或晚期绑定的调用回退为调用真实方法。建议原文的关键句：

> VB already recognizes certain method calls and emits them as intrinsic IL instructions. It's just a matter of expanding the list and defining a well-known module in the runtime with the right shared methods in the right shapes to make more of IL expressible through VB.（Anthony 18.23，逐字引用）

**PROPOSAL B — 主流指令自然语法、魔法模块只留给长尾。** 把建议自己的那句"mainstream instructions should be surfaced more naturally"（Anthony 18.23，逐字引用）贯彻到底：给 Checked/Unchecked 与 Volatile 字段**自然语法**（C#-alignment，见下文 2017.08.23 的 `Option Checked` 线索），魔法模块只服务于真正不值得关键字的长尾 opcode。

**PROPOSAL C — 不激活，拆解回各自主张者。** 四场景各自回家：大小写敏感解析 → 独立设计（`Probably` No Plans，见 Q&A）；Checked/Unchecked → 独立小提案（C#-alignment，与 #86 / 2014 优化共享实现面）；Volatile → `Threading.Volatile` + 可选的字段修饰符议题；Localloc → 无需求数据，保持搁置；`System.Runtime.Intrinsics` → 已是普通方法、JIT 认识，先确认语言层是否真的有事做。

**OTHER DESIGNS CONSIDERED**

- **IL Literals（在源码里写 IL）**：Anthony 明确拒绝，我们也拒绝。它属于"在 VB 里嵌入另一种语言"的家族——2018.02.07 会议对 #211（使用任意已安装语言）的裁定是 *"Fantastic idea, and too hard to do."*（逐字引用），IL Literals 是更窄的同类，难度不减、收益还小。
- **为每个 opcode 加一次性关键字**：Anthony 厌倦的就是这个；我们同意关键字膨胀是坏方向。但 **DirectCast/TryCast 先例反过来提醒我们**：某些指令"太主流"，**值得**关键字——所以"绝不加关键字"和"给所有 opcode 加关键字"之间，还有 B 这条中间路。
- **什么都不做 / 维持现状**：继续靠 `DirectCast`/`TryCast` + BCL 方法（`Threading.Volatile`、`Math.BigMul`）表达零星能力。零成本；代价是 Checked 算术等真实缺口继续空着。

### 权衡：Q&A

- **A 的机制是真是假？** 真的，但它是**优化家族的既成事实**，不是新发明。Roslyn 对 `CInt(Math.Truncate(d))` 的 `conv.ovf.i4` 降级（2014 决议）、CLR 对 `Threading.Volatile.Read/Write` 与 `Interlocked` 的识别，都是同一条线。**我们的分歧不在机制存在，而在机制被抬升成"语言表面"的那一步。** #86 把这条线钉死在"optimization, not a feature"；A 恰恰把它变成 feature（新模块 + 新方法 = 新 API 表面 + 新语义承诺）。
- **A 的优雅回退点：真实方法必须存在。** 若 well-known 模块里的方法**是真实方法**（BCL 里有实现），那么：精确形状早期绑定 → 降级为指令；形状不符/晚期绑定 → 正常调用真实方法。这是 A 最漂亮的性质——它天然消化了晚期绑定与形状不符两条分叉，不需要任何运行期开关。**但这个性质是有价格的：指令与真实方法必须语义逐位一致。** 2014 决议就带着这条 TODO：*"verify that `conv.i4` has exact same semantics as `CInt(Math.Truncate)`, including negative values, superlarge doubles and exceptions, NaN, Infinity"*（逐字引用）。每扩一条内联列表，都要做一次这种等价性证明；扩展得越多，**等价性维护责任**越重——这不是一次性成本，是永久负担。
- **A vs B：四场景是捆着好还是拆开好？** 拆。①大小写敏感解析**根本不是指令**——它连 A 的机制都装不进，却占着 A 的名单头条；③Volatile 的核心缺口（字段修饰符）是**声明面**，方法调用形态也装不进。一个机制装不下四分之三的场景，只能说明这四件事被"都能表达更多 IL"这句口号硬凑在了一起。**We think：A 的机制只对真正的指令缺口（②④）成立，而这两个缺口里又只有一个（②）有真实需求证据。**
- **Checked/Unchecked 应该走哪条路？** 这是全场最接近"真实缺口"的一块。2017.08.23 会议在讨论方法级 `Option` 语句时已经留过一句：*"This could dove-tail with an `Option Checked`."*（逐字引用）。C# 有 `checked`/`unchecked` 关键字；VB 走关键字或 `Option Checked` 都是 C#-alignment 的自然语法，读起来是 *"检查溢出"* 而不是 *"调用某个神秘方法"*。而且 2017.04.12 会议记录过 `CType` 的缺口：不支持 *"Integer <-> Char (AscW, ChrW)"、 "Truncation"、"Unchecked integral conversion (chopping)"*（逐字引用）——checked/unchecked 不只是 `+ - * /` 的溢出模式，还牵涉**转换**（`conv.ovf.i4` 族），把 2014 决议、#86、本建议三块连成一片实现面。**结论：checked/unchecked 值得做，但做法是 B（自然语法），不是 A（魔法模块）。** `Runtime.Instructions.AddOvf(left, right)` 读起来不是 VB。
- **Volatile 到底缺什么？** 方法级读写已经被 `Threading.Volatile.Read/Write` 覆盖（编译器/JIT 识别它们）。缺的是 **volatile 字段修饰符**——那是声明语法（`Dim _flag As Volatile Integer`？），不是方法调用，A 的"well-known 模块 + 方法形状"**结构上表达不了声明修饰符**。C# 有 `volatile` 字段；VB 没有。若要做，是独立的 C#-alignment 议题。
- **Localloc 为什么不顺手做掉？** 它是最纯粹、也最危险的指令。栈上分配的生命周期是当前方法帧：C# 用 `stackalloc` 语法（配 `Span(Of T)`），且在 **Async 方法里禁止**（状态机把局部帧搬上堆）、在 `Try` 区域与表达式树里有额外限制。A 的"返回形状"本身就没定义：返回 `IntPtr` 还是 `Span(Of Byte)`？`Span` 是 ref struct，受存储规则约束，进不了字段/闭包/Async。需求数据为零。**这不是"顺手"，这是一个设计面，值得它自己的提案，且激活前需要真实场景。**
- **`System.Runtime.Intrinsics` 的"共享基础设施"指什么？** 我们翻遍了 vblang 会议存档，没有检索到任何对 SIMD / `System.Runtime.Intrinsics` / `Vector128` 的直接讨论（`Suspect`/`OPEN QUESTIONS`，见下）。事实层（`Probably`）：这些是带 `[Intrinsic]` 特性、由 JIT 识别的**普通方法**，任何语言都能调用——VB 今天就能写 `Vector128.Create(...)`。所以"共享基础设施"若指**语言层**，我们目前没有证据表明缺什么；若指 **JIT/运行时层**，那不是 VB LDM 的地盘，应委托 C# LDM 与运行时团队（我们一贯"CLR/库层变更直接委托 C# LDM"）。
- **well-known 模块的名字会撞车吗？** 会。若模块放在 `Microsoft.VisualBasic` 命名空间且被自动打开，用户代码里声明 `Instructions` 就会破坏——2014.02.10 会议处理 `Console` 自动打开时记过同类账：*"NOTE: THIS IS A BREAKING CHANGE: e.g. you will no longer be able to declare variables of type 'Console'. Doesn't seem a very bad breaking change."*（逐字引用）。**规则必须是不自动导入、用户显式 `Imports`**；即便如此，用户命名空间里若有同名模块，识别规则必须是"精确解析到 well-known 模块且作用域内无用户候选"，否则静默改绑。
- **识别既有调用会破坏旧代码吗？** 会——这正是 #86 的核心裁定。优化 `CInt` 这种**既有调用**，重编译后行为/性能变化即 breaking，所以 *"this is a breaking change and can't be done"*（逐字引用）。**推论：若激活 A，内联列表只能新增"今天还不存在的 well-known 方法"，绝不能重判既有方法调用。** 这两条路线必须写死，否则就是原则 #7 的正面教材。
- **表达式树怎么办？** #86 已经给过答案：*"Expression trees themselves will be unchanged. The IL output of expression trees is not expected to be high performance. Thus, plan to do what is easy. If code is shared, the optimization will also happen in the IL generation from expression trees."*（逐字引用）——表达式树节点保持**方法调用**，指令发射只发生在非树路径；这要求真实方法存在且语义等价（否则树编译与直接编译行为分叉）。对 localloc 类指令，表达式树**根本不能表示**，必须报错。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

本建议**不新增任何文法**——这是它唯一的、也是最大的语法优点：它借用既有的方法调用语法。但代价是新出现一类**"什么时候算魔法调用"**的判定歧义：

```vb
' 用户自己的模块也叫 Instructions，同名同形状：
Module Instructions
    Public Function AddOvf(a As Integer, b As Integer) As Integer
        Return a + b
    End Function
End Module

Dim r = Instructions.AddOvf(1, 2)
' 若编译器无条件把"同名同形状"的调用识别为指令，这里会静默改变行为——不可接受。
' 规则：仅当调用精确解析到 well-known 模块的方法、且作用域内无用户候选时，才降级。
```

`Probably`：识别判定需要在绑定完成后做（不是词法），且必须是"精确形状 + 模块身份 + 无遮蔽"三条件合取。模块名本身会事实性半保留（类似 `System.Runtime.CompilerServices` 的约定式保留），这属于"软保留字"而非文法改动。

#### 2. 角案例与边界语义

逐个场景过角案例：

- **Checked/Unchecked 的形状爆炸。** `add.ovf` 有符号/无符号、定长/溢出语义的多种变体；`conv.ovf.i1/.i2/.i4/.i8` 及其无符号版（2014 决议已列出）。一个 `AddOvf` 不够，需要一组方法；而方法名无法可靠传达"这是 `add.ovf` 还是 `add.ovf.un`"——这是魔法方法形态的固有笨拙。**We think：这正是"主流指令值得自然语法"（B）的最直接证据。**
- **Volatile 的形状。** `Threading.Volatile.Read(Of T)` 是泛型；若 well-known 模块引入泛型魔法方法，精确形状匹配要覆盖**泛型实例化**。v1 若做，`Probably` 应先排除泛型。
- **Localloc 的返回形状未定义。** 返回 `IntPtr`（`Probably` 不友好）还是 `Span(Of Byte)`（ref struct，存储受限）？原文未说。而且 localloc 在 **Async/迭代器**里必须报错（状态机搬帧使栈分配无效），在 `Try` 区域有 GC hole 风险——规则未定。
- **`AddressOf` 与委托转换。** `AddressOf Runtime.Instructions.AddOvf` 必须绑定到**真实方法**（不能"降级"成没有地址的指令）。同理，把魔法调用塞进委托创建表达式，应保持真实方法。
- **整数溢出与常量折叠。** `Runtime.Instructions.AddOvf(Integer.MaxValue, 1)` 若被常量折叠，折叠路径也要遵守 checked 语义——降级与常量折叠的先后顺序必须定义。

#### 3. 作用域与绑定

语义模型里，魔法调用返回**真实方法的符号**（`GetSymbolInfo` 指向 well-known 模块的方法），降级是**代码生成阶段**的决定，而不是绑定阶段换符号——这一点比 `DirectCast`（那是有专用文法的语法）更干净，因为它对 IDE/分析器透明。成员访问的 `GetTypeInfo` 返回真实方法的返回类型；`Me`/属性求值时机与普通调用一致。**不存在"绑到 property 还是 backing field"的问题**——因为这里是方法调用，不是成员收窄（与 TypeOf 流分析会议的绑定面完全不同）。

#### 4. 与既有特性的交互

- **Late binding / Option Strict Off**：spec 明确 *"When the target of a member access expression or index expression is of type `Object`, the processing of the expression may be deferred until run time"*（逐字引用，spec/expressions.md）——经 `Object` 调用的魔法方法**不得降级**，走运行期真实方法查找。真实方法存在 ⇒ 优雅回退，不抛 `MissingMemberException`（除非用户真把它删了）。
- **`Call` 语句 / 指令风格调用**：`Call Runtime.Instructions.AddOvf(a, b)` 是合法 VB，应同样识别（降级不关心调用语句形态）。
- **表达式树**：保持方法调用（#86 裁定），见 Q&A。
- **Async / 迭代器**：localloc 类指令禁止；Checked/Volatile 无碍。
- **与 `proposal-runtime-library.md`（`Microsoft.VisualBasic.CompilerServices`）**：VB 运行时里已经有一个编译器认识的 helper 命名空间（spec 的 statements.md 引用了 `Imports Microsoft.VisualBasic.CompilerServices`）。`Probably`：其中的 `Operators` 类就是"well-known 模块 + 共享方法"的现成先例（编译器在对象算术/宽松路径上调用它），但逐方法签名我们无法在本仓库核实——这条线索应传给 runtime-library 提案去确认。

#### 5. Breaking change 与兼容性

三层，层层分开：

1. **新增 well-known 方法并识别之** → 旧代码无影响（新方法今天不存在，不会重编译变化）。安全。
2. **重判既有方法调用**（如 #86 想优化 CInt）→ 重编译行为/性能变化 = breaking，#86 已裁定 *"can't be done"*。**禁止。**
3. **自动导入 well-known 模块** → 破坏用户同名类型（`Console` 先例）。**禁止自动打开，必须显式 `Imports`。**

**We 要求**：任何复活版本都必须把这三条写进 Compatibility 章节，并配 `langversion` 门控——否则"魔法"从"优化"滑向"隐蔽语义变化"，就是原则 #7 拒绝 `Return?` 时的同一句话：*"Control flow would be altered by a very subtle character."*（逐字引用，2018.05.30 会议 #167）。

#### 6. Option Strict / 编译选项分叉

降级识别是**早期绑定专属**。Strict On：精确形状的早期绑定调用识别并降级；Strict Off：同样的调用早期绑定成立时**同样识别**（识别不依赖 Strict 选项），经 `Object` 的调用保持晚期绑定、不降级。两条路径对"已识别的调用"行为一致；唯一的差异是"能否走晚期绑定路径"，那是 Option Strict 本身的分叉，不是本特性新增的。**无新分叉。**

#### 7. IDE / IntelliSense 影响

- 补全：well-known 模块的方法会出现在 IntelliSense 列表里，与普通方法**无法区分**——需要在文档气泡里标注"lowered to `add.ovf`"，否则开发者会把指令映射当普通方法读。
- 调试：降级后的指令**没有方法体可 Step Into**；断点落在调用行，但"进入方法"无事发生。溢出异常（`OverflowException`）的栈轨迹与真实方法调用不同（没有中间帧）——调试体验差异必须写成文档。
- PDB：指令没有自己的序列点；源码行映射仍需保持。

#### 8. 数据 / 普遍性

这是全场最需要诚实的地方。2018.05.30 会议对用户群的画像：*"there are hundreds of thousands of quiet customers each month primarily want VB to keep doing what it does now"*（逐字引用）。对照四场景：

- **Checked 算术**：金融/嵌入式/协议代码有真实诉求，但**没有任何量化数据**；VB 默认回绕几十年，业务代码里主动要 checked 的占比未知。
- **大小写敏感解析**：只对"写得很烂的库"成立，且 VB 的大小写不敏感是**文化基因**（见 `proposal-case-insensitivity.md` 同族讨论），需求面窄。
- **Volatile 字段**：多线程代码的窄子集。
- **Localloc**：需求面最窄，无数据。

**结论：普遍性全部存疑。** 这不是"头条特性"的材料，`Suspect`：Anthony 看到的一遍遍重复请求（"tired of being asked"）很可能来自**同一个技术小众群体**，而非"数十万安静客户"。

#### 9. 更简替代

- **Analyzer**：提示"此处可能溢出"或"此转换可优化"——#86 讨论过 *"Do we want an analyzer that finds slow CInts? It would change the semantics of the program. Probably won't do this."*（逐字引用）；溢出警告类 analyzer 不改语义，可行。
- **BCL 方法**：`Threading.Volatile`、`Math.BigMul`（检查乘法）、`MidpointRounding`——方法级缺口大半已堵。
- **C#-alignment 自然语法**：`checked`/`Option Checked`（2017.08.23 线索）、`Volatile` 字段修饰符——两条最干净的替代，都属于 B。
- **纯优化路线（#86 家族）**：把"已存在、语义等价"的调用优化成指令（`CInt(Int(...))` 族），不做任何新表面——这是机制价值的**最保守兑现**，也是我们唯一不设防的路线。

#### 10. 复杂度 / 成本 / 优先级

- 识别表 + 代码生成降级：Roslyn VB 里是中等工作量。
- well-known 模块（命名空间/类/方法签名）+ spec + **每个 opcode 的等价性证明**：大工作量，且等价性维护是永久负担。
- 四场景逐一实现的成本差异极大：checked/unchecked 是"B 小提案 + 共享 2014/#86 实现面"；localloc 需要一个完整设计面。
- **优先级：低。** 它在队列里的位置应在模式匹配、可空性流分析、ShapeOf 之后；而且它的最大价值（checked 算术）已经有独立主线线索（`Option Checked`）。

#### 11. 运行时 / CLR 硬约束

- `localloc`：可验证 IL 中受尺寸约束允许；但 Async 状态机搬帧、`Try` 区域 GC hole、表达式树不可表示——硬约束真实，设计面完整后才谈得上。
- `volatile.`：仅对字段/ByRef 位置有效；`Threading.Volatile` 方法形态已在 JIT 层识别。
- `add.ovf` 族 / `conv.ovf` 族：PEVerify 无碍；溢出模式正确即可。
- **不触达存储规则**（除 localloc 的生命周期）；无表达式树新增问题（保持方法调用）。

#### 12. 值不值得做

逐维打分。**作为单一语言特性**：价值中低（唯一有真实需求的是 checked 算术，而它不值得一个"魔法模块"）、成本中高（模块 + 等价性维护 + 规则面）、风险中高（魔法调用的隐蔽性、大小写敏感的基因冲突、模块撞车）。**作为机制方向的存档**：价值高——它诚实记录了"优化家族"的边界，并为我们指明了 checked/unchecked 这条最该走的细分线。**结论：不激活为特性，保留为方向注记。**

### VB 基因对照

- **保持 VB-like（原则 #2）**：**分裂。** 方法调用形态本身"看起来像 VB"（这是 A 的卖点），但 ①大小写敏感解析**直接违背** VB 的大小写不敏感文化基因——#211 会议上我们自己也承认过 *"the easiest of these differences to visualize are case sensitivity and overloads resolution"*（逐字引用）。一个以"大小写不敏感"为身份特征的语言，为一个"写得烂的库"引入大小写敏感绑定，门槛应极高。
- **不引入"第二种做事方式"（原则 #3）**：A 是"第二种做事方式"的重灾区——它让"写 IL 级操作"有了第三条路（P/Invoke `Declare`、`DirectCast`/`TryCast`、魔法方法）。但 **B 与纯优化路线不是**：B 给 checked 一个自然的检查溢出表达，纯优化只是让既有调用更快。
- **避免隐蔽的控制流/语义变化（原则 #7）**：**核心风险。** "方法调用被悄悄换成指令"就是隐蔽语义变化的极致形态。`Return?` 被拒的理由（*"Control flow would be altered by a very subtle character."*）在这里以"更隐蔽的方式"重现——不是字符层面，是**符号层面**。没有"精确形状 + 无遮蔽 + 不重判既有调用"三重约束，A 就是语言级的陷阱。
- **消除常见样板（原则 #9）**：仅 checked 算术沾边（省去每次手写检查辅助方法）；且 `a + b` 本身已经够短，checked 变体加的是仪式不是减仪式。命中很弱。
- **冗长只在有用时是美德（原则 #10）**：DirectCast/TryCast 先例表明**显式就是美德**——"主流指令应被自然暴露"。`Runtime.Instructions.AddOvf(a, b)` 是"无用的冗长"；`checked(a + b)` 是"有用的冗长"。
- **默认跟随 C#，除非有充分理由（原则 #4）**：C# 对 checked/volatile/localloc 全部用**自然语法**（`checked`、`volatile` 字段、`stackalloc`），没有一个用魔法模块。VB 若用魔法模块，是"偏离 C# 且没有充分理由"。
- **不为边缘场景加特性（原则 #6）**：localloc 是纯边缘；volatile 字段偏窄。直接指向"不发明特性"。
- **与主线关系（对照表 2.3）**：对照表无直接对应行。最接近的两条轴线：**性能与互操作**（主线 #86 的"优化而非特性"裁定、2014 `conv.i4` 决议、DirectCast/TryCast 先例）与**默认跟随 C#**（checked/volatile 的关键字路线）。本建议是 **Anthony 独立延伸**（第 18 章实验；主线没有把"识别表 + well-known 模块"泛化为语言表面的计划）；其中 checked/unchecked 与主线 2017.08.23 的 `Option Checked` 线索**方向一致**，大小写敏感解析与主线 #211 讨论**同题**（且我们倾向 No Plans），`System.Runtime.Intrinsics` 属委托 C# LDM/运行时的领域。

### RESOLUTION:

1. **不激活 `proposal-native-instruction-helpers` 为单一语言特性。** 四场景异质（绑定规则 / 指令缺口 / 声明面 / 生态问题），捆绑违反"边界清晰"；机制方向真实但属"优化家族"，泛化到"well-known 模块 + 新语言表面"须逐个场景证明价值，而非一次打包。
2. **机制判定**：编译器认识方法调用并降级为原生指令的机制**真实且已有先例**（DirectCast/TryCast → unbox/isinst；2014 `conv.i4` 决议；#86）。先例把这条线钉死在 **"Treat it as an optimization, not a feature"**；A 越过了这条线。若将来激活，**只可新增"今天不存在"的 well-known 方法，绝不重判既有方法调用**（#86 裁定 breaking can't be done）。
3. **拆解去向（各场景回各自的家）**：
   - **Checked/Unchecked 算术**：最有前景的一块，独立成小提案，走 **C#-alignment 自然语法**（`checked`/`unchecked` 关键字或 `Option Checked`——2017.08.23 已留 dove-tail 线索）；与 2014 `conv.ovf` 优化、#86 共享实现面；补上 2017.04.12 记录的 `CType` 缺口（Truncation、chopping、`Integer <-> Char`）的转换面。
   - **大小写敏感成员解析**：独立设计，`Probably` **No Plans**——与 VB 大小写不敏感基因冲突（#211 佐证）、非指令机制、需求窄；若要做，须先回答"为什么一个以大小写不敏感为身份的语言要为烂库开洞"。
   - **Volatile**：方法级已被 `Threading.Volatile` 覆盖；缺的是 **volatile 字段修饰符**（声明面，C#-alignment 议题），魔法模块结构上表达不了。
   - **Localloc**：数据不足，保持搁置；激活需真实场景 + 完整设计面（返回形状、Async/迭代器禁令、`Try` 区域规则）。
   - **`System.Runtime.Intrinsics`**：已是普通方法、JIT 识别；"共享基础设施"若指语言层**目前无证据**，标 `OPEN QUESTIONS`，转 C# LDM/运行时团队核实。
4. **若将来以任一场景激活 A 的机制，必须满足的约束**（写进 spec）：
   - 识别 = 精确形状 + 模块身份 + **作用域内无用户候选**（防静默改绑）。
   - well-known 模块**不自动导入**，用户显式 `Imports`（防 `Console` 式破坏）。
   - **真实方法必须存在且语义逐位等价**，等价性验证按 2014 决议的 TODO 清单模式逐 opcode 建档；表达式树保持方法调用（#86）。
   - 经 `Object` 的晚期绑定调用**不降级**，回退真实方法。
   - localloc 类指令在 Async / 迭代器 / 表达式树中报错。
   - `langversion` 门控 + 警告策略，配 Compatibility 章节。
5. **保持 inactive**，定义为"机制方向存档 + 拆分器"。它真正的产出不是一份可实现的提案，而是把四场景分别移交到正确的工作项。
6. **激活所需信号（明确列出）**：
   - (a) 选择**一个**试点场景（建议 checked/unchecked）并给出：可编译示例 + IL 映射表 + 等价性验证清单（2014 TODO 模式）；
   - (b) well-known 模块形状草案（命名空间/类名/方法签名）与**撞车规则**的完整答案；
   - (c) 数据：真实 VB 代码库中 localloc / volatile 字段 / 大小写敏感解析的量化需求（取代"tired of being asked"轶事）；
   - (d) 与 `Option Checked` / 项目级 check-overflow 设置的交互设计。

### Implication:

- 更新 `proposal-native-instruction-helpers.md`：补"四场景拆解表"、标注与 #86 / 2014-03-12 / DirectCast-TryCast 先例的关系、写死三条 breaking 禁令、把未决问题逐项挂到目标工作项。
- 为 checked/unchecked 立一份**新的小提案**（B 路线），与 `Option Checked` 线索、2014 `conv.ovf` 决议、#86 串成一条实现线；这是本建议唯一值得"升级"的细分线。
- 向 `proposal-runtime-library.md` 传一条线索：`Microsoft.VisualBasic.CompilerServices`（`Operators` 类）是"well-known 模块 + 共享方法"的现成先例，核实逐方法签名后可作为机制参照。
- 与 `proposal-case-insensitivity.md` / `proposal-name-resolution.md` 对齐：大小写敏感解析若有人主张，去那边开新线程，不在本建议名下复活。
- 不建原型；不新增文法；不新建"魔法模块"实现。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：`System.Runtime.Intrinsics` 的"共享基础设施"到底指什么——我们检索 vblang 会议存档**未发现**任何 SIMD/Intrinsics/`Vector128` 讨论（`Suspect`：主线从未在这个面上设计过）；需与 JIT/运行时团队确认"语言层是否有事可做"，还是纯粹委托 C# LDM。
- `OPEN QUESTIONS`：若激活，well-known 模块的命名空间/类名/方法签名长什么样；撞车规则（用户同名模块遮蔽）的精确判定。
- `OPEN QUESTIONS`：等价性维护的责任边界——编译器（每次降级前验证）还是运行时（保证真实方法语义=指令语义）？#86 显示"语义不同就做不了"，但责任归属未定。
- `TODO`：量化四场景需求（激活信号 (c) 的证据来源）。
- `TODO`：核对 `Microsoft.VisualBasic.CompilerServices.Operators` 的逐方法签名（`Probably` 存在但本仓库无法逐字核实），作为机制先例文档。
- `Follow-up`：checked/unchecked 小提案与 2014 `conv.ovf` 工作项、#86、`Option Checked` 线索的合并实现面。
- `Follow-up`：与 `proposal-runtime-library.md`、`proposal-case-insensitivity.md` 对齐本建议移交的场景归属。

### 状态

- **LDM 状态：保持 Inactive**；转为"方向存档 + 拆分器"角色——本建议不独立排期，四场景各自找家。
- **三态判定：Table（保持搁置）** — 不是 Reject（机制真实、checked/unchecked 是真实缺口、与主线 #86/`Option Checked` 呼应、无直接破坏面），也不是 Active（无可实现内容、四场景捆绑、无数据、大小写敏感解析与 VB 基因冲突）。激活信号见 RESOLUTION #6，逐条待检。

---

## 附录：特性评价

# 建议评价报告：proposal-native-instruction-helpers.md

## 评价对象

- 建议：proposal-native-instruction-helpers.md — 平台原生指令辅助（扩展内联列表 + 运行时 well-known 模块，让更多 IL 可经 VB 表达）
- 来源：Anthony 原文第 18 章 18.23 "Platform Native Instruction Helpers"（`..\..\AnthonyDesign_wordpress.txt` L3244–3274，引文逐字）
- 配方目标：在不新增关键字、不引入 IL Literals 的前提下，把更多 CLR 指令能力经 VB 表达；目标场景为大小写敏感成员解析 / Checked-Unchecked 算术 / Volatile / Localloc

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 2/5 | 锚点 2："Motivation 泛泛而谈，改进不可衡量，示例不能演示改进"。动机真实（厌倦逐个 opcode 加关键字）但**零示例、零语法、零量化**；"扩展列表 + well-known 模块"是机制陈述不是可衡量改进；四场景各自的目标强度均未定义 | 已提供/已检查（状态行 Prototype/Implementation/Specification 为占位链接，无运行证据） | 声称的效果与"四场景中三件不是指令缺口"的事实未对齐；无任何可演示载体 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。四场景捆绑且性质异质（绑定规则/指令/声明面/生态）；大小写敏感解析**直接违背** VB 大小写不敏感基因；机制本身借用 C#/CLR 的 JIT-intrinsic 模型与 P/Invoke 框架却未点名 | 已检查 | 若被实现成魔法模块，特性维滑向 1（隐蔽语义变化）；唯一正向是"零新语法"与 DirectCast/TryCast 先例继承 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、来源引述准确、3 个未决问题具体诚实（1–3 健康区间）；但 Detailed design **无任何代码示例**（违反模板）、无文法/spec 方向、模块形状与方法列表完全留白、Drawbacks 泛泛、无 Compatibility 分析 | 已检查 | 状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）；关键边界（识别规则、模块命名、等价性）全部未定 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对（明显破坏兼容/一致性断裂）"。大小写敏感解析 = 与 VB 身份的一致性断裂且无对冲；魔法调用的隐蔽性损害调试/迭代（雷）；无维度明显受益；撞车风险（模块名）未识别 | 已检查（预测待定） | 文档未对冲误读风险——"扩展列表"容易被读成"重判既有调用"（#86 已裁定 breaking）；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注"。来源标注准确（18.23 引文、vblang spec unbox/isinst、`System.Runtime.Intrinsics`、OpCodes 文档）；但未标注关键相邻成分：C#/CLR 的 JIT-intrinsic 参照模型、P/Invoke 框架的借用、主线 #86 与 2014 `conv.i4` 决议这两条直接先例、`Microsoft.VisualBasic.CompilerServices` 现成模块先例 | 已检查 | 无杂质；但成分关系不透明——读者无法从文档看出"这是优化家族的泛化"，也就无法判断它踩了 #86 的哪条线 |

## 设计原则对照

- **与 VB 基因：部分一致、核心偏离。** 一致：零新语法（借既有方法调用，符合 #2/#10 的表面）、DirectCast/TryCast 先例（"主流指令应自然暴露"）。偏离：#7（隐蔽语义变化）是核心风险，魔法调用=符号层版的 `Return?`；①大小写敏感解析直接违背大小写不敏感文化基因（#2）；localloc 违反 #6（边缘场景）；"第三种做事方式"踩 #3。
- **与主线关系：分裂。** 机制先例**主线一致**（2014 `conv.i4` 决议、#86 "optimization, not a feature"、DirectCast/TryCast）；泛化到"well-known 模块 + 语言表面"是 **Anthony 独立延伸**（主线无此计划）；checked/unchecked 与主线 2017.08.23 `Option Checked` 线索**方向一致**；大小写敏感解析与主线 #211 讨论同题且倾向 No Plans；`System.Runtime.Intrinsics` 属委托 C# LDM/运行时领域。与 `proposal-runtime-library.md`（CompilerServices 模块）交互，与 `proposal-case-insensitivity.md` 同族异轴。
- **破坏性变更：直接无（新方法、新模块），潜在三类。** ① 重判既有调用（#86 已裁定 can't be done）；② 自动导入模块（`Console` 先例）；③ 魔法调用改变可观察行为（异常栈、调试）。文档完全未分析。

## 总评

- **达成程度：未达成（作为可采纳特性）/ 部分达成（作为机制方向存档）。** 机制真实且有先例链，但文档没有任何可采纳的实现内容，且四场景捆绑使边界模糊；唯一的"升级"线索（checked/unchecked）被埋在捆绑里。
- **LDM 三态建议：Table（保持 inactive，转"方向存档 + 拆分器"）。** 面向 VBScript.NET 的优先级同样为 **Table**：脚本/业务代码的主流路径不需要一个"魔法指令模块"；checked/unchecked 可单独以 C#-alignment 小提案跟进（Consider），localloc/volatile/大小写敏感解析保持搁置（Table/`Probably` No Plans）。
- **主要问题**：① 四场景异质捆绑，三分之二装不进机制本身；② 大小写敏感解析与 VB 基因直接冲突且非指令机制；③ 机制从"优化"跨进"语言表面"的那一步踩了 #86 的线，文档未识别；④ 零示例、零语法、零数据，无可执行内容；⑤ 撞车/等价性/表达式树/晚期绑定交互全部未设计。

## 返工建议

- **补充章节**：Detailed design（现仅机制一句）——给出至少一个场景的可编译示例与 IL 映射表（建议选 checked/unchecked）；补 Compatibility 章节（三条 breaking 禁令 + `langversion` 门控）；补"四场景拆解表"与先例关系（2014/#86/DirectCast-TryCast）。
- **补充证据**：四场景需求量化（激活信号 (c)）；`System.Runtime.Intrinsics` 在 VB 代码生成路径上是否有阻碍的核实（委托 C# LDM/运行时）；`Microsoft.VisualBasic.CompilerServices.Operators` 逐方法签名（机制先例文档）。
- **未决问题处理**：三个 Unresolved 要么在本建议回答，要么显式指向目标工作项（模块命名/撞车 → 若激活；等价性责任 → 与运行时团队；`System.Runtime.Intrinsics` 基础设施 → C# LDM）；"扩展列表"必须显式限定为"只新增 well-known 方法，不重判既有调用"。
- **设计探索**：checked/unchecked 作为独立小提案（B 路线）的完整设计——`Option Checked`（2017.08.23 线索）与 `checked` 关键字的取舍、与 2014 `conv.ovf` 转换面（Truncation/chopping/`Integer <-> Char`）的合并；这是本建议唯一值得投入的后续线程。

---

## 附录：C# 生态与互操作考量

> 本附录由 ModVB 评估项目 meeting 追加 agent 撰写，基于 `..\..\..\csharplang-index.md` 与 `..\..\..\csharplang` 镜像逐字核实。目标：给出「C# 现实方向 vs 本提案响应」的对应，判断兼容 / 冲突 / 需桥接 / 脱节，并评估对正文 RESOLUTION / 三态判定的影响。**只追加，不改动正文。**

### 相关 C# 现实方向

#### F1. 函数指针 `delegate*<...>`：C# 对「暴露 IL 指令」的正式答案（C# 9）

C# 对「把 IL opcode 暴露给语言」的正式答案不是魔法方法，而是**类型化的函数指针** `delegate*<...>`。proposal 的 Summary 动机（逐字）：

> This proposal provides language constructs that expose IL opcodes that cannot currently be accessed efficiently, or at all, in C# today: `ldftn` and `calli`. These IL opcodes can be important in high performance code and developers need an efficient way to access them.（→ proposals\csharp-9.0\function-pointers.md，Summary）

关键事实：

- 取地址走 `ldftn`：「The address-of operator will be implemented using the `ldftn` instruction.」（→ function-pointers.md，Allow address-of to target methods）
- 调用走 `calli`：`delegate*` 的调用映射到 `calli`，delegate 的调用则映射到 `callvirt`（→ function-pointers.md，Detailed Design）。
- 调用约定支持 `unmanaged[Cdecl]`、`unmanaged[Stdcall, SuppressGCTransition]` 等，经 `System.Runtime.CompilerServices` 的 `CallConv*` 类型编码为签名 modopt。
- **但 `delegate*` 是指针类型**：「A `delegate*` type is a pointer type which means it has all of the capabilities and restrictions of a standard pointer type:」→ 第一条即「Only valid in an `unsafe` context.」（→ function-pointers.md，Detailed Design）

#### F2. 被拒的「compiler intrinsics」：C# 曾设计过魔法机制，最终拒绝

function-pointers 提案是 **compiler intrinsics** 被拒提案的替代设计（原文：「This is an alternate design proposal to [compiler intrinsics].」→ function-pointers.md，Motivation）。被拒提案（→ proposals\rejected\intrinsics.md）的 Summary 与本提案的机制高度同构：

> This proposal provides language constructs that expose low level IL opcodes that cannot currently be accessed efficiently, or at all: `ldftn`, `ldvirtftn`, `ldtoken` and `calli`. These low level opcodes can be important in high performance code and developers need an efficient way to access them.（→ proposals\rejected\intrinsics.md，Summary）

它曾设计 `&` 作用于方法组返回 `void*`（`ldftn`）、`handleof`（`ldtoken`）、`CallIndirectAttribute`（`calli`）。C# 最终**放弃**了这条路，改采类型化 `delegate*`。拒绝理由散见 Considerations 的 "Using delegates" 一节：`Func<int>*` 这类「delegate 类型 + 指针标注」编码到元数据时缺少唯一类型、跨程序集 OHI 等价性无法保证，因此需要独立的函数指针类型。更重要的是——**C# LDM 早在 2017-01-11 就讨论过「把声明识别为 intrinsic」的语言机制**（「Recognize declarations as intrinsics, and implement them」「Enforce whichever special rules apply on call」，raw notes，→ meetings\2017\LDM-2017-01-11.md），最终被采纳的形态仍是类型化函数指针，而非通用的 intrinsic 声明 / 魔法方法机制。

#### F3. `[UnmanagedCallersOnly]`：只能经函数指针调用的方法

C# 提供 `System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute`，让方法只能通过函数指针调用（供 native 回调）。proposal 原文（逐字）：

> `System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute` is an attribute used by the CLR to indicate that a method should be called with a specific calling convention.（→ function-pointers.md，UnmanagedCallersOnly section）

> It is an error to directly call a method annotated with this attribute from C#. Users must obtain a function pointer to the method and then invoke that pointer.（→ function-pointers.md，UnmanagedCallersOnly section）

这是「方法本身不可在托管侧直接调用」的元数据级事实：任何语言（含 VB）读取到该特性，都不应允许直接调用，而应要求经函数指针间接调用。

#### F4. `System.Runtime.Intrinsics`：JIT 识别的方法，语言层无参与

`System.Runtime.Intrinsics`（`Vector128<T>` 等）是带 `[Intrinsic]` 特性、由 JIT 识别的**普通方法**——语言层没有特殊语法。..\..\..\csharplang 中对 Intrinsics/SIMD/`Vector128` 的直接语言讨论几乎为零（仅 `proposals\rejected\intrinsics.md` 的 Summary 提及一次 `ldtoken` 等，以及 `meetings\2017\LDM-2017-01-11.md` 的编译器 intrinsic 原始笔记——见 OPEN QUESTIONS）。这印证索引 T3/M1 的判定：**「方法调用 + 编译器/JIT 识别」的机制在 .NET 生态真实存在，但它是运行时/库层事实，不是语言特性**——这正是本提案「well-known 模块」机制的 C# 侧镜像，而 C# 刻意不把它做成语言表面。

#### F5. unsafe evolution（C# 15 候选）：把 unsafe 重定义为「解引用非托管内存」

C# 正在把 `unsafe` 从「出现指针」改为「解引用非托管内存」。与本提案最相关的五条：

- **函数指针调用永远需要 unsafe 上下文**（逐字）：

> Function pointers are not yet incorporated into the main C# specification, but they are similarly affected; everything but function pointer invocation is moved into the standard specification. A function pointer invocation expression must always occur in an `unsafe` context.（→ proposals\unsafe-evolution.md，Pointer types）

- **需要 unsafe 上下文的表达式清单**（逐字，含函数指针调用与特定 `stackalloc`）：

> The following expressions require an `unsafe` context when used: Pointer indirections; Pointer member access; Pointer element access; Function pointer invocation; Element access on a fixed-size buffer; `stackalloc` under the conditions defined below.（→ proposals\unsafe-evolution.md，Redefining expressions that require unsafe contexts）

- **`stackalloc` 基本变得安全**，仅三条件合取才 unsafe（逐字）：

> A _stackalloc_expression_ is unsafe if all of the following statements are true: The _stackalloc_expression_ is being converted to a `Span<T>` or a `ReadOnlySpan<T>`. The _stackalloc_expression_ does not have a _stackalloc_initializer_. The _stackalloc_expression_ is used within a member that has `SkipLocalsInitAttribute` applied.（→ proposals\unsafe-evolution.md，Stack allocation）

- **对 VB 的明确表态**（索引第四节已核实，逐字）：

> We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either.（→ proposals\unsafe-evolution.md，VB）

- **新元数据 `MemorySafetyRulesAttribute`（模块级，标 `15`）与 `RequiresUnsafeAttribute`（成员级）**：opted-in 程序集中未标 `RequiresUnsafeAttribute` 的成员，调用它不需要 unsafe 上下文（逐字）：

> Any member in such an assembly that is not marked with `RequiresUnsafeAttribute` does not require an `unsafe` context to be called, regardless of the types in the signature of the member.（→ proposals\unsafe-evolution.md，Metadata）

Compat mode 下还有一条对「指针/函数指针出现在签名」的兜底规则（逐字，节选）：

> ...a member is considered *requires-unsafe* if it contains a pointer or function pointer type somewhere among its parameter types or return type (can be nested in a non-pointer type, e.g., `int*[]`).（→ proposals\unsafe-evolution.md，Compat mode）

#### F6. 低层互操作配套基础

- `nint`/`nuint`（C# 9）：动机原文「The motivation is for interop scenarios and for low-level libraries.」（→ proposals\csharp-9.0\native-integers.md，Summary）。
- `unmanaged` 约束（C# 7.3）：「The primary motivation is to make it easier to author low level interop code in C#.」（→ proposals\csharp-7.3\blittable.md，Motivation）。
- `Span<T>`/ref struct 安全模型（C# 7.2）：「The main reason for the additional safety rules when dealing with types like `Span<T>` and `ReadOnlySpan<T>` is that such types must be confined to the execution stack.」（→ proposals\csharp-7.2\span-safety.md，Introduction）。
- ref struct 接口（C# 13）：localloc 的天然载体 `Span(Of T)` 是 ref struct，「The inability for `ref struct` to implement interfaces means they cannot participate in fairly fundamental abstraction techniques of .NET.」（→ proposals\csharp-13.0\ref-struct-interfaces.md，Motivation）。

### 现实 vs 提案

| 本提案场景 | C# 现实方向 | 判定 | 理由 |
|---|---|---|---|
| ② Checked/Unchecked 算术 | 自然语法 `checked`/`unchecked`（safe，无 unsafe 边界） | **兼容（B 路线）/ 脱节（A 路线）** | C# 用关键字而非魔法方法表达溢出；PROPOSAL B（`Option Checked`/`checked`）即 C#-alignment；魔法模块形态无 C# 对应 |
| ③ Volatile | `volatile` 字段修饰符（声明面）+ `Threading.Volatile.Read/Write` 方法（JIT 识别） | **兼容** | 方法级已被 BCL 覆盖；字段修饰符缺口与 C# 声明面同向 |
| ④ Localloc | `stackalloc` 语法（C# 7.2 起配 `Span<T>`），unsafe-evolution 后基本安全 | **需桥接** | C# 的返回形状答案是 `Span(Of T)`（ref struct）；VB 无 ref struct、无存储规则，无法复用，需独立设计面（且需求数据为零） |
| ① 大小写敏感成员解析 | C# 天然大小写敏感，无对应「指令」机制 | **脱节** | 与 C# interop 无关，印证正文「①不是指令问题」 |
| `System.Runtime.Intrinsics`「共享基础设施」 | JIT 识别的普通方法，语言层无缺（F4） | **兼容且无语言层工作** | VB 今天即可调用；印证正文 Q&A 的 `Probably`，OPEN QUESTION 收敛为运行时/JIT 层 |

**机制层面的总体判定（与正文 RESOLUTION #2 呼应）**：C# 的现实是——「编译器认识某调用并降级为指令」的机制在 .NET 生态**真实存在**（JIT 对 `Volatile`/`Interlocked`/`[Intrinsic]` 方法的识别，F4），但 C# **拒绝**把该机制做成**语言表面**（rejected/intrinsics.md，F2），转而用类型化函数指针 + 自然语法。这与本提案 RESOLUTION 的「优化家族，不是 feature」完全同向：**C# 先例给「不激活 A」提供了跨语言的额外佐证**，而非冲突。

### 对 VBScript.NET 的适应建议

1. **默认安全、按需动态**：.vbx 把「安全 IL 出口」作为默认（checked/volatile 等 safe opcode 可经自然语法或 BCL 方法表达），把任何指向未托管内存的操作（`calli`、解引用、裸 `localloc` 返回指针）排除在语言表面之外——unsafe-evolution 已明确 VB 无指针、无 requires-unsafe（F5）。若「魔法模块」只用于 safe opcode，与 C# 的 requires-unsafe 边界不冲突；想暴露 `calli`/裸 `localloc` 则因 VB 无 unsafe 上下文而**结构性不可表达**。
2. **source-gen 桥**：需要裸指针/函数指针才能表达的场景（P/Invoke、native 回调），VBScript.NET 的出口是 **source generator**——生成 C#/IL 侧的函数指针 + `[UnmanagedCallersOnly]` 包装，脚本侧保持普通方法调用。这与索引 T4/T6 的「source-gen 替代运行时 marshaling」方向一致，也规避了 VB 无 unsafe 上下文的问题（F3、F5）。
3. **识别新元数据（必须桥接）**：.NET 11+ 后 C# 程序集会带 `MemorySafetyRulesAttribute` 与 `RequiresUnsafeAttribute`（F5），且 C# 指针/函数指针会更多地在非 unsafe 上下文出现。**VB/VBScript.NET 编译器必须认识这两个特性**，才能正确判定「调用 C# 的 requires-unsafe 成员」——尽管 VB 自身无 unsafe 上下文，判定结果应是「拒绝/诊断」，否则会静默放行对 requires-unsafe 成员的调用，破坏安全审计语义。这是决策文件 M8 点名的硬桥接点。
4. **消费 C# 函数指针元数据**：若 C# 库签名出现 `delegate*`（ECMA-335 method pointer type），VB 编译器读取元数据时应能识别并给出受限诊断（VB 无等价类型）；Compat mode 下含函数指针的签名即视为 requires-unsafe（F5），VB 调用方应收到「无法安全调用」的诊断。
5. **`nint`/`nuint` 与 `unmanaged` 约束**：`.vbx` 的互操作层可消费 C# 的 `nint`/`nuint`（= `IntPtr`/`UIntPtr` 别名）与 `unmanaged` 约束泛型（F6）——这些是元数据级既有事实，VB 只需不阻碍。

### 对既有 RESOLUTION / 三态判定的影响

- **三态判定（Table）不受动摇，且多一条 C# 侧证据**：C# 拒绝 compiler intrinsics（F2）证明「把指令暴露做成语言表面」在 C# 的既定裁决里同样不采纳——本提案的 A 路线与 C# 现实**同向被否**，Table（保持搁置）判定更稳。
- **RESOLUTION #2「优化家族」判定获得跨语言佐证**：C# 的 `[Intrinsic]`/JIT 识别（F4）说明「方法调用被编译器/JIT 特殊对待」是生态既成事实，但 C# 刻意不把它语言化——正文「A 是优化家族的既成事实，不是新发明」得到独立印证。
- **RESOLUTION #3 拆解去向的微调建议**：
  - checked/unchecked（B 路线）与 C# `checked`/`unchecked` 关键字**直接对齐**，无互操作风险；
  - localloc 拆解时补充「C# 的答案是 `stackalloc` + `Span(Of T)`，VB 因无 ref struct 无法复用，需独立设计面」——这是对正文「返回形状未定义」的 C# 侧补充；
  - `System.Runtime.Intrinsics` 的 OPEN QUESTION 可从「语言层是否有事可做」收敛为「运行时/JIT 层是否有 VB 专属障碍」，结论倾向**没有**（F4）。
- **新增一条激活约束**：若将来以任一场景复活 A 的机制，well-known 模块的**方法清单必须避开 C# 的 requires-unsafe 边界**——凡是映射到 `calli`/解引用/裸 `localloc` 返回指针的形态，在 VB 里无 unsafe 上下文可用，等价于不可表达；可安全表达的只有 checked/volatile 等 safe opcode 族。这条应并入正文 RESOLUTION #4 的激活约束清单。

### 引用纪律与 OPEN QUESTIONS

- 本附录所有 C# 原文均**逐字**摘自 `..\..\..\csharplang` 镜像并标注 `→` 来源；索引第四节 6 段中直接使用了 native-integers、blittable、span-safety、ref-struct-interfaces、unsafe-evolution(VB) 五段，function-pointers 与 unsafe-evolution 的长引文经原文文件逐行核实。
- `OPEN QUESTIONS`：..\..\..\csharplang 中 `System.Runtime.Intrinsics` 的**语言层**讨论——检索仅命中 `proposals\rejected\intrinsics.md`（Summary 提及低层 opcode 语境）与 `meetings\2017\LDM-2017-01-11.md`（编译器 intrinsic 原始笔记，raw notes 未清理），无 SIMD/`Vector128` 的成熟语言设计正文；「JIT 对 `[Intrinsic]` 方法的识别」属 dotnet/runtime 领域，本库无正文，无法在本仓库核实。
- `OPEN QUESTIONS`：`[LibraryImport]` source generator 与 `safe` 修饰符的交互（unsafe-evolution 已答「允许 `safe` 作为声明修饰符，用于所有 `unsafe` 可标 requires-unsafe 之处」，→ LDM-2026-07-22），但该决定对 VB 侧 source-gen 桥的精确生成形态的影响未深挖。
