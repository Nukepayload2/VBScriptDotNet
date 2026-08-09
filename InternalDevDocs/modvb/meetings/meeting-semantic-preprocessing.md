# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周我们把"语义预处理"摆上台面——一份想给条件编译加语义谓词（`TYPE_EXISTS` / `MEMBER_EXISTS` / `METHOD_EXISTS`）的建议。它比表面看起来复杂得多：话题跨越预处理指令（2014 年主线刚就 `#Disable Warning` 定过调子）、条件编译的常量模型、以及 2017 年主线明确划给源码生成器的 `INotifyPropertyChanged` 样板场景。我们用了相当篇幅把这三块旧账拉齐，再判断本建议在多大程度上是"新能力"，在多大程度上是"把生成器该干的活搬回语言"。

## Agenda

* [Proposal: 语义预处理 `##If`（Semantic Pre-Processing）](#proposal-语义预处理-##if-semantic-pre-processing)

## Proposal: 语义预处理 `##If`（Semantic Pre-Processing）

_Related: [vblang #219 – Implementing INotifyPropertyChanged is Tedious](https://github.com/dotnet/vblang/issues/219)；[vblang 2017-11-15 – `Replaces` w/ source generators 讨论](`../../vblang/meetings/2017/vbldm-notes-2017.11.15.md`)；[vblang 2014-04-16 / 07-01 – `#Disable Warning`](`../../vblang/meetings/2014/LDM-2014-04-16.md`)；ModVB sibling：`meeting-ignore-warning-directive.md`（`#Ignore Warning`，已 Table）；ModVB sibling：`proposal-semantic-preprocessing.md`_

### 场景与缺口

We started from the observation that the proposal's Motivation is about a real, long-standing pain: 同一份共享代码（shared projects / linked-files）要跨框架版本与平台适配，今天只能靠编译符号手工管理条件编译，而符号与"API 是否真的存在"往往脱节。版本符号能表达"框架版本 >= X"（SDK 风格项目自动定义 `NET*_OR_GREATER` 这类符号），但表达不了"这个类型/成员在目标引用集里是否存在"——尤其当类型的出现不随版本单调时（新版本删掉某 API、或仅存在于特定平台包）。

```vb
' 今天：#If 依赖符号，符号与真实 API 面脱节。
#If NET8_0_OR_GREATER Then
    obj.FastPath()
#Else
    obj.SlowPath()
#End If

' 建议：##If 依赖语义谓词，贴合真实 API 面。
##If METHOD_EXISTS(obj.BetterMethod) Then
    obj.BetterMethod(...)
##Else
    ' Must call obsolete method in this case.
    #Ignore Warning BC40008
    obj.OldSlowMethod(...)
##End If
```

提案还叠加了三个衍生用途：**条件编译声明**（`##If TYPE_EXISTS(...)` 决定是否声明某重载）、**生成器样板补充**（`##If Not MEMBER_EXISTS(...)` 让生成器"若开发者没声明就补"）、以及 **`#Warning` / `#Error` 嵌套**（模板/生成器在 `##If` 分支内报诊断）。

We think the *缺口* 是真实的——"该 API 在目标平台上是否存在"确实不是版本符号能表达的信息。但我们在候选方案阶段就开始担心：这份建议把**四个彼此独立的能力**（语句条件、声明条件、生成器样板、自定义诊断）捆进了一个未定义核心语义的 `##` 指令族。We 决定逐层拆开看。

### 候选方案

**PROPOSAL A — 按建议原文引入 `##If` 指令族。** 新记号 `##` + 三个语义谓词 + `#Warning` / `#Error` 嵌套。技术上最诚实的形态：`##If` 分支**总是被解析**，随后由语义通道求值并丢弃不存活的分支——这正好避开"扩展 `#If` 会打破词法器跳过非活跃区域模型"的坑。代价是第二个条件编译机制、以及一整条新的编译流水线段。

**PROPOSAL B — 扩展既有 `#If`，不引入 `##`。** 在现有 `#If` 的条件表达式里加内建语义函数：`#If TypeExists("System.DateTimeOffset") Then` / `#If MemberExists("PropertyChanged") Then`。理由：原则 #3（不引入"第二种做事方式"）——`#If` 已经能条件编译语句和声明，只是它的条件是**常量表达式**；把它扩展为"常量 + 语义谓词"比另起一个 `##` 记号少一张脸。代价：`#If` 从词法阶段的常量判断变成两阶段（先全量解析、后语义求值），Roslyn 词法器"非活跃区域不产生树"的模型被打穿，且同一指令族里"哪些条件走常量、哪些走语义"用户必须自辨。

**PROPOSAL C — 不加语言特性，把能力放进 SDK / 分析器 / 生成器。** 版本场景由 SDK 派生的 `NET*_OR_GREATER` 符号继续覆盖；"API 是否存在"由生成器用 `Compilation.GetTypeByMetadataName` / `ISymbol.GetMembers` 在**生成器自己的代码**里探测并产出条件化代码；另加一个"符号漂移检测"分析器，警告 `#If` 符号与实际 API 面脱节。语言表面为零。代价：生成器作者负担重（提案的 Alternatives 自己也承认），且手写共享代码的"自适应"仍要手维护符号。

**PROPOSAL D — 什么都不做。** 保留 `#If` 符号模型，把跨目标适配留给使用者自建符号表。作为对照保留。

### 权衡：Q&A

- **`##` 是不是"第二种做事方式"？** 是。VB6 / VBScript 年代以来，条件编译就是 `#Const` + `#If ... #ElseIf ... #Else ... #End If` 一个机制。2014 年主线为 `#Disable Warning` 讨论指令设计时，把"纯文本宏 / 常量模型"当作 `#If` 的既有资产看待。本建议引入 `##`，等于对同一件事给出第二条语法路径——设计原则 #3 的门槛极高。提案的 Drawbacks 自己也承认"与现有 `#If` 纯文本宏的简单模型分叉"，但把这个分叉当作可接受的代价陈述，而非需要辩护的决定。We 认为这正是最需要辩护的地方。
- **技术分叉能成为引入 `##` 的理由吗？** 我们承认有一条**真实的技术分叉**：语义谓词不能在词法阶段求值，所以 `##If` 分支必须全量解析、后置丢弃；而 `#If` 靠词法器跳过非活跃区域。若把语义谓词塞进 `#If`，要么接受 `#If` 从"常量判断"变成"两阶段"，要么维护一个用户不可见的分派。但"技术分叉"不是"用户该看到两个记号"的理由——它更像实现内部的事。`##` 双井号在视觉上不像任何 VB 惯用法（读起来像 C/C++ 的记号粘连，C# 没有 `##` 运算符），不符合"读起来像英语"。
- **"syntax-only，无需语义分析"成立吗？** 不成立。提案的 Motivation 写"Enables source generation from syntax w/o requiring semantic analysis"，但 `MEMBER_EXISTS(PropertyChanged)` 本身就是一条语义查询——编译器必须绑定当前类型、合并 partial、探测成员存在。真正发生的只是把探测工作**从生成器代码搬回编译器**：生成器不再写 `GetMembers(...)`，改成在产出文本里留一个 `##If`，让编译器替它查。这是**劳动的再分配**，不是"免语义分析"。而且它把劳动再分配的**时机**问题（见下文 partial/生成器顺序）变成语言级契约。
- **生成器样板场景该不该归语言？** 我们引用了主线先例。2017-11-15 讨论 `INotifyPropertyChanged` 三提案时，主线明确说："We're fairly confident that if we had `Replaces` w/ source generators we wouldn't do `Bindable` or `WithPropertyEvents` as `INotifyPropertyChanged` is basically the poster child for the source generator feature."，并决定 "We need to know the status of source generators and how far out they are before deciding."。提案的 `ToolGenerated.vb` 示例（`##If Not MEMBER_EXISTS(PropertyChanged)` 补事件声明）恰好是把这个**已划给生成器的场景**拉回语言指令。生成器本就有完整的 `Compilation` 语义模型，"缺什么补什么"在生成器代码里几行就够；`##If` 在这里是便利，不是能力。
- **`#Warning` / `#Error` 嵌套是否构成独立理由？** 不构成。`#Warning` / `#Error` 今天已能在 `#If` 块内使用（由词法阶段决定是否存活）。语义版自定义诊断（"平台行为不同"、"缺反序列化构造函数"）本质是分析器/生成器输出诊断的问题，不需要语言开指令。
- **sibling 依赖要不要算分？** 提案示例用了 `#Ignore Warning BC40008`，而 sibling 建议 `#Ignore Warning` 已被我们判定为 Table，其 RESOLUTION 明确要求本建议把该示例改写为 `#Disable Warning BC40008` / `#Enable Warning BC40008` 两行式。We 视此为反向责任：不因"另一份建议假设它存在"而放行本建议，也不因 sibling 被 Table 而连坐本建议——但示例必须改。
- **"adaptive light-up"的承诺是否被高估？** 我们一致认为**声明级**的"light-up"是编译期适配，不是运行时适配：`##If TYPE_EXISTS(MyType)` 决定的是"这份二进制是否引用 MyType"，不是"这份二进制在缺 MyType 的机器上还能跑"。同一源码、不同目标 → 不同二进制面——这正是提案想要的效果（库作者按目标出不同 API 面），但它**没有**改变部署模型，只改变了条件**维护**模型（条件跟踪真实 API 而非手工符号）。这个价值真实，但比"自适应点灯"的说法更节制。更有甚者，若谓词基于引用集判定"存在"而部署运行时真的缺该类型，得到的不是优雅降级而是运行期 `TypeLoadException`——提案的 `#Warning` 反射示例其实是在给这个洞打补丁。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`##` 作为指令起始记号在词法上可分（今天的 VB 里 `##` 是词法错误，`##If` 没有既有含义），纯增量。但真正的语法问题在**谓词参数**：

```vb
' 谓词参数是方法组——VB 里这不是合法表达式（只有 AddressOf / 委托上下文能引用方法组）。
##If METHOD_EXISTS(obj.BetterMethod) Then   ' obj.BetterMethod 作为裸表达式不合法。
```

`obj.BetterMethod` 在 VB 中不是可独立出现的表达式。提案的 Unresolved questions 第一个就承认参数形态未定（对象表达式 / 类型名 / 字符串成员名），但这正是 spec 的核心，不是边角。`TYPE_EXISTS(System.DateTimeOffset)` 形如类型名（可类比 `GetType(System.DateTimeOffset)` 的参数）——这条是干净的。`MEMBER_EXISTS(PropertyChanged)` 是裸标识符——在"与常量表达式重名""成员名恰为关键字"等情形下需要上下文判定，且与 `#If` 的常量条件（标识符 = 编译常量）视觉上无法区分。`##If` 与 `#If` 的**嵌套/组合**关系未定义：`##If` 条件里能否写符号？若能，`##If` 就是 `#If` 的超集，为何要两个记号；若不能，组合要靠嵌套两个指令族，用户要同时掌握两套。2014-02-17 已确认预处理指令内不允许隐式换行续行（"Neither Roslyn nor Native allow implicit line continuations in preprocessor directives"）——任何多行谓词形态都被这个约束排除。

#### 2. 角案例与边界语义

- **类型存在性的解析目标**。`TYPE_EXISTS(System.DateTimeOffset)` 对"哪个编译视图"求值？必须是**当前编译的引用程序集 + 当前编译自身**。但"当前编译自身"意味着谓词能看见正在声明的类型——`TYPE_EXISTS(MyOwnType)` 是恒真吗？规范需要钉死。类型转发（type forwarder）下 `System.DateTimeOffset` 在引用集里存在、运行程序集里却缺失——谓词按哪一边判？`Probably`：按引用集判（与编译器"类型可解析"同口径），但这与运行期 `TypeLoadException` 风险（见 Q&A）绑定。
- **成员存在性**。`MEMBER_EXISTS(OnNameChanged)`：查当前类型还是含基类？含私有成员吗？含其他 partial 文件里声明的成员吗？若目标成员就是**本指令所在分支要声明的成员**（自引用），是恒假还是错误？这些全未定义。而"缺失即 False 而非报错"要求一种**探针绑定模式**（probe binding）：谓词以"成员不存在 = False"而不是"成员不存在 = 编译错误"来绑定。这是新的绑定模式，不是既有绑定的复用。
- **语句级谓词的绑定上下文**。`METHOD_EXISTS(obj.BetterMethod)` 出现在方法体内，`obj` 是上方声明的局部变量——谓词必须在**所在块的部分绑定上下文**中求值。这意味着编译流水线要先绑定局部，再求值指令，再丢弃分支，再全量绑定。两遍绑定。
- **surface flip（声明面翻转）**。谓词翻转会静默改变公共 API 面：

```vb
' 引用集变化（升级目标框架 / 更新包）→ 分支翻转 → 重载面改变。
##If TYPE_EXISTS(Company.LegacyThing) Then
    Public Sub Save(value As Company.LegacyThing)
    End Sub
##Else
    Public Sub Save(value As Object)
    End Sub
##End If
```

升级后 `LegacyThing` 消失 → `Save` 从强类型重载变为 `Object` 重载 → 同一源码、同一调用点，重载决议与消费方可见面**双双改变**。对库作者这正是要的"按目标出不同面"；但对消费方（和 IDE 缓存）这是**没有源码改动就发生的行为变化**——设计原则 #7（避免隐蔽的语义变化）的红旗。缓解必须包含：谓词钉在**引用程序集**、保证确定性、`langversion` 门控、以及"哪些分支存活"的 IDE 可视性。

#### 3. 作用域与绑定

`##If` 求值结果的符号形态：被丢弃分支里的声明**不进语义模型**（等价于不存在）；存活分支的符号正常可见。但语义模型对"丢弃分支内的代码"返回什么——错误还是 `null` 符号？`#If` 的模型是"词法器不产生非活跃区域的树"，天然无符号；`##If` 是全量解析后再丢弃，树存在、符号为空，IDE 必须专门处理"有树无符号"的灰色区域。**partial / 生成器顺序**是本节最硬的问题：`MEMBER_EXISTS(PropertyChanged)` 要看到开发者在另一个 partial 文件里的声明，就必须在**所有源文件 + 生成器输出合并之后**求值——即编译构造完成之后。于是流水线是：解析 → 跑生成器 → 组装编译 → 探针绑定 → 求值指令 → 丢弃分支 → 全量重绑定 → 发射。**两遍完整绑定**。这不是"从语法出发免语义分析"，这是一条新编译阶段，且与增量编译、后台编译的交互完全未设计。

#### 4. 与既有特性的交互

- **`#If` 常量条件编译**：核心交互。`##If` 是否接受符号条件、是否允许 `##If` 套 `#If` 再套 `##If`，未定义。若 `##If` 是 `#If` 的超集，则两个记号纯属冗余。
- **`#Disable Warning` / `#Enable Warning` / `#Warning` / `#Error`**：这些指令由词法阶段判定存活。放进 `##If` 分支后，存活判定移入语义阶段——`#Error` 在语义阶段发射等于自定义编译错误，需要定义其与 `TreatWarningsAsErrors`、`/nowarn` 的交互。sibling 已 Table 的 `#Ignore Warning` 不能再用作示例（见 Q&A）。
- **Late binding / Option Strict Off**：`Option Strict Off` 下 `obj` 可声明为 `Object`，`METHOD_EXISTS(obj.BetterMethod)` 在编译期对 `Object` 无从判定（成员是运行期解析的）：

```vb
Option Strict Off
Let obj As Object = GetTarget()
##If METHOD_EXISTS(obj.BetterMethod) Then   ' 编译期不知道 Object 上有无该方法——谓词对动态类型无定义。
```

- **扩展方法 / `Shadows` / 重载**：`METHOD_EXISTS` 看得见扩展方法吗？`MEMBER_EXISTS` 对 `Shadows` 成员、同名重载（方法 vs 属性 vs 事件同名）怎么数"存在"？未定义。
- **源码生成器**：见 2017-11-15 引文——主线已把样板场景判给生成器；本建议把该场景拉回语言，且引入生成器产物的"时机契约"。

#### 5. Breaking change 与兼容性

词法层面纯增量（`##` 此前是词法错误，新指令不改变任何合法代码的既有含义）。但有两个方向的破坏：① **surface flip**——谓词翻转静默改变声明面/重载面，对消费方是"无源码改动的 breaking"；需 `langversion` 门控 + 警告策略，建议文档完全没有。② **可复现性**——同一源码 + 同一 TFM，但 `PackageReference` 版本不同 → 引用集不同 → 分支不同 → 输出不同。对"自适应"这是特性，对"可复现构建"这是债，必须显式写入 spec。提案未做任何兼容性分析。

#### 6. Option Strict / 编译选项分叉

严格/宽松两路径必须行为一致，但 `METHOD_EXISTS(obj.X)` 在 `Option Strict Off` 且 `obj As Object` 时**在编译期无解**（见 4）。`TYPE_EXISTS` 的类型名解析在两条路径下应一致（宽松模式的隐式 `Object` 不应改变类型存在性判定）。这两条都要写进 spec，提案只字未提。

#### 7. IDE / IntelliSense 影响

`##If` 的"存活分支灰显"要求 IDE 对每个谓词做一次语义求值——而用户编辑时语义模型可能未就绪，参考集变化（加引用）会翻转整套存活分支，触发全文件重分析。这与 Anthony 自评的"高度依赖分析器/生成器技术性能"呼应，但把性能风险从"生成器作者承担"转嫁给"每次编辑的 IDE 管道承担"。另外"有树无符号"的灰色区域（见 3）需要新的 IDE 状态与文案。

#### 8. 数据 / 普遍性

提案没有任何量化数据。"跨版本/跨平台共享代码"的真实用户是库作者与跨平台应用作者——是"数十万安静客户"中的子集。而且版本场景的 85% 已被 SDK 派生的 `NET*_OR_GREATER` 符号覆盖（`Probably`，这是 .NET SDK 的既有行为，非本会议新论断）；真正的增量是"API 存在性"这一小类。主线原则 #6（不为边缘场景加特性）要求价值足够大才配得上这么大的设计面。`Suspect`：痛点是真实的，但普遍性证据强度不足。

#### 9. 更简替代

- **PROPOSAL C**：生成器用 `Compilation.GetTypeByMetadataName` / `GetMembers` 探测——能力相同、零语言表面，且生成器本来就有语义模型。这是对"生成器样板"场景的正面替代。
- SDK 派生符号 + "符号漂移检测"分析器：覆盖版本场景的大部分，并直接治"符号与 API 脱节"的根。
- MSBuild `<Choose>/<When>`：把判断留在项目文件——提案 Alternatives 已承认"语义仍未知"，此路不通。
- 结论：在"更简替代"这一条，提案的独特增量只剩下"**手写共享代码**里按 API 存在性自适应"——这是唯一没有现成替代的角落，但也正是设计负担最重的角落。

#### 10. 成本 / 优先级

实现成本极高：新指令族（或 `#If` 两阶段化）+ 谓词语法 + 探针绑定模式 + 两遍完整绑定 + IDE 存活分支/灰色区域 + 增量编译适配。这是"新编译阶段"级别的工程量，不是"优化既有路径"级别。价值是子集场景的符号维护减轻。成本 × 风险（surface flip、Option Strict 分叉、顺序契约）与价值的匹配度，在我们看来**现在是负的**。优先级应排在所有"让既有惯用法更聪明"的建议之后——这是新能力，不是优化。

#### 11. 运行时 / CLR 硬约束

无直接 CLR 约束（纯编译期）。但有一个部署模型约束值得记：谓词基于**引用集**判"存在"而部署运行时可能真缺该类型，得到的是 `TypeLoadException` 而非优雅降级——"adaptive light-up"因此是编译期适配，不是运行时适配。这条已在 Q&A 论证。

#### 12. 值不值得做

价值：真实但窄（跨目标库作者的手写共享代码；生成器样板场景已被 2017-11-15 划给生成器）。成本：极高（新编译阶段 + 探针绑定 + IDE）。风险：高（surface flip breaking、Option Strict 分叉、partial/生成器顺序、性能）。**现在不值得作为 Active 特性做。** 但"API 存在性不可由版本符号表达"这个缺口真实且未被任何主线工作覆盖——它值得保留为一个受控的研究/原型项，而不是像 `#Ignore Warning` 那样直接压到 Table 以下。

### VB 基因对照

- **不引入"第二种做事方式"（原则 #3）**：直接冲突。`##If` 是 `#If` 之外的第二条条件编译路径；把已划给生成器的样板拉回语言指令更是反向。这是本建议最大的基因问题。
- **避免隐蔽的语义变化（原则 #7）**：surface flip（引用集变化静默改变 API 面）与"谓词求值依赖编译上下文"都是隐蔽语义变化。`#If` 的常量模型之所以安全，正因为条件在词法阶段可见；语义谓词把它推进了语义阶段。
- **读起来像英语、对新手友好（原则 #5）**：`##` 双井号不读如英语，且"何时用 `#If` 何时用 `##If`"对新手是额外概念负担。`TYPE_EXISTS(System.DateTimeOffset)` 倒是读得通——但那是谓词措辞的功劳，不是 `##` 记号的。
- **不为边缘场景加特性（原则 #6）**：动机面向子集（跨目标共享代码作者），无普遍性数据。
- **消除常见样板（原则 #9）**：唯一正向——符号维护是真实样板。但样板是"维护条件"而非"写代码"，且 SDK 符号 + 漂移分析器可部分消解。
- **继承 VB6/VBScript 的血缘**：条件编译是 VB6 / VBScript 的 `#Const` + `#If ... #End If` 直接遗产（本建议未标注这一血缘）。语义谓词是对该遗产的**语义化延伸**——方向上是"让既有惯用法更聪明"，但引入新记号使它退化为"另起炉灶"。
- **与主线关系（对照表 2.3）**：主线在条件编译上是"纯常量简单模型"的现状，在 `INotifyPropertyChanged` 样板上是"交给源码生成器"的已决方向（2017-11-15）。本建议属 Anthony 独立延伸，**与主线"样板归生成器"的方向张力**；与 `#Ignore Warning`（sibling，已 Table）耦合。

### RESOLUTION:

1. **动机成立，特性不成立（现状）。** "该 API 在目标平台上是否存在"是版本符号无法表达的真实缺口；但本建议把语句条件、声明条件、生成器样板、自定义诊断四个独立能力捆进一个未定义核心语义的 `##` 指令族，范围不清、谓词文法/探针绑定/partial-生成器顺序全未定义。`##` 作为"第二种条件编译方式"违反原则 #3。
2. **`##` 记号否决（Reject）**：不引入第二个条件编译指令族。若未来推进，扩展既有 `#If`（PROPOSAL B），并接受 `#If` 从"纯常量"变为"常量 + 语义谓词"的两阶段模型——这一代价需要原型证实，现未证实。`##` 双井号同时不符合原则 #5。
3. **`METHOD_EXISTS(obj.Member)` 对象表达式谓词否决（Reject）**：方法组不是 VB 合法表达式（除 `AddressOf` / 委托上下文），且依赖静态类型、在 `Option Strict Off` / `Object` 下无定义。任何最小可行形态都仅限 `TYPE_EXISTS(typename)` 与 `MEMBER_EXISTS(name)`（当前类型内、裸标识符或字符串）。
4. **生成器样板用途划归生成器（Table）**：对齐 2017-11-15 主线决定——`INotifyPropertyChanged` 是 "the poster child for the source generator feature"，生成器用 `GetMembers` 等语义 API 在生成器代码里探测即可，不需要语言开指令。"syntax-only，无需语义分析"的表述不成立（`MEMBER_EXISTS` 本身就是语义查询），应改写为"把探测工作从生成器移回编译器"，并重新评估该劳动再分配是否值得一个语言特性。
5. **自定义诊断不构成独立理由**：`#Warning` / `#Error` 已可在 `#If` 内使用；语义版自定义诊断由分析器/生成器产出即可。
6. **surface flip 是主要 breaking 风险，任何推进都必须先解决**：谓词必须钉在**引用程序集**上、保证确定性（同源码 + 同引用集 → 同输出）、配 `langversion` 门控与"哪些分支存活"的 IDE 可视性。这要求在 spec 里定义"引用集快照"作为谓词求值的稳定输入。
7. **三态：Consider**。不 Reject——"API 存在性不可由版本符号表达"的缺口真实且未被主线覆盖，这是 `#Ignore Warning`（Table）不具备的正面价值。不 Active——成本（新编译阶段 + 探针绑定 + 两遍绑定 + IDE）极高、证据不足、设计未成熟。**唯一续判信号**：① 两阶段流水线（先组装编译、再探针求值、再丢弃分支）的最小原型 + 增量/后台编译性能数据；② 跨目标共享代码作者的真实痛点量化（当前为零）；③ partial/生成器顺序的明确规则（谁先看到谁的声明）。

`Not all of us are happy with` Consider——有人主张直接 Table（"成本与风险双高，且最干净的子场景已被生成器接管，剩下的'手写共享代码按 API 自适应'是最窄的角落"），也有人主张把最小可行形态（`#If` 扩展 + `TYPE_EXISTS`/`MEMBER_EXISTS`）单独立项做一次受控原型（"生成器拿走 80%，剩下 20% 恰好是语言最该管的"）。We did not find the evidence strong enough either way to move beyond Consider。

### Implication:

- 起草"最小可行 speclet"（不进入语言，仅供原型）：以 `#If TypeExists(typename)` / `#If MemberExists(name)`（扩展既有 `#If`）为形态，定义探针绑定模式（缺失即 False）、引用集钉定、`langversion` 门控、partial 合并顺序、Option Strict 分叉。
- 更新 `proposal-semantic-preprocessing.md`：`#Ignore Warning BC40008` 示例改两行式（`#Disable Warning BC40008` / `#Enable Warning BC40008`）；修正原文 `#End If`（单 `#`）笔误为 `##End If`；补 Grammar（BNF）、Compatibility、Option Strict 三节；移除示例中的 `...` 占位符。
- 将"生成器样板"场景移交生成器工作项，对齐 2017-11-15 待办（"investigate status of source generators and revisit"）。
- 评估 PROPOSAL C 的"符号漂移检测"分析器，作为对"符号与 API 脱节"的更简替代——零语言表面。
- 在 OPEN QUESTIONS 记录续判信号与需核实的既有行为。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：`#If` 的常量表达式是否支持 `AndAlso` / `OrElse` 短路运算符（Roslyn 文法核实；影响"`##If` 条件可否混写符号"的讨论）。
- `OPEN QUESTIONS`：探针绑定模式（缺失即 False）与既有报错绑定的界线——同一表达式在谓词内与外行为不同，IDE 语义模型如何呈现。
- `OPEN QUESTIONS`：谓词对"当前编译自身类型 / partial 合并 / 生成器产物"的可见性顺序——谁先看到谁的声明。
- `OPEN QUESTIONS`：surface flip 的 `langversion` 门控与警告策略的具体形态（每处翻转报新警告？还是整体开关？）。
- `TODO`：量化跨目标共享代码/链接文件的真实占比与符号维护事故率，为普遍性补证据。
- `TODO`：两阶段流水线的最小原型（含增量/后台编译性能基线）。
- `Follow-up`：与 `meeting-ignore-warning-directive.md` 同步——该会议 RESOLUTION 已要求本建议示例改两行式，改完需重新走自身评价。
- `Follow-up`：与源码生成器工作项对表——2017-11-15 的"revisit"待办至今未关闭（主线原始笔记即如此记录），本建议的样板场景应挂到该待办之下。

### 状态

- **LDM 状态：Consider**——缺口真实（API 存在性不可由版本符号表达），但设计未成熟、成本极高、与主线"样板归生成器"方向张力；`##` 记号、对象表达式谓词分别否决，最小可行形态（`#If` 扩展）留作受控原型。
- **三态判定：Consider**——不 Reject（正面价值存在且主线未覆盖）；不 Active（新编译阶段级别的成本与 surface flip 风险，证据不足）；按续判信号（原型性能数据 / 痛点量化 / 顺序规则）决定上移或搁置。

---

## 附录：特性评价

# 建议评价报告：proposal-semantic-preprocessing.md

## 评价对象

- 建议：proposal-semantic-preprocessing.md — 语义预处理 `##If` / `TYPE_EXISTS` / `MEMBER_EXISTS` / `METHOD_EXISTS`
- 来源：Anthony 原文第 13.4 节 "Semantic Pre-Processing"（`..\AnthonyDesign_wordpress.txt` L2312–2381；全部示例与自评"highly depending on the performance of certain analyzer or source generator techniques"逐字来自该节）
- 配方目标：条件编译从"基于编译符号"升级为"基于语义谓词"，支持语句/声明条件编译、生成器样板补充与 `#Warning` / `#Error` 嵌套

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。Motivation 的缺口（API 存在性不可由版本符号表达）真实；语句/声明条件编译在概念上可操作；但核心承诺"source generation from syntax w/o requiring semantic analysis"不成立（`MEMBER_EXISTS` 是语义查询，只是把探测从生成器移回编译器）；"adaptive light-up"实为按目标编译期的二进制适配，非运行时适配 | 已检查 | 无原型/运行封顶 3；关键子效果（免语义分析）消退；声明级 surface flip 的 breaking 风险未分析；性能依赖自认未量化 |
| 特性 | 2/5 | 锚点 2："外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。`##` 双井号非 VB 惯用（视觉像 C/C++ 记号粘连，C# 无 `##` 运算符）；与既有 `#If` 构成"第二种条件编译方式"（原则 #3 冲突）；四个能力（语句条件/声明条件/生成器样板/自定义诊断）捆绑进一个指令族，无一单独定稿 | 已检查 | 未继承并延伸 VB6/VBScript `#Const`+`#If` 简单模型，而是另起 `##` 记号；`##If` 与 `#If` 的嵌套/组合关系未定义；谓词参数（方法组裸表达式）与 VB 表达式文法冲突 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、示例与 Anthony 13.4 原文逐字一致、4 个未决问题具体诚实（如实列出是加分）；但谓词文法/BNF 缺失、作用域与绑定整体未定义、无 Compatibility 章节、"syntax-only"与 Detailed design 自相矛盾、示例含 `...` 占位符与 `Let obj As MyType = ...` | 已检查 | 无文法；无 breaking-change 分析；状态行占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）；原文 `#End If`（单 `#`）笔误被作者自己标记为疑似（诚实，但说明 spec 未定稿） |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。雷/光正向（跨目标共享代码提速、盘活 `#If` 知识）；暗风险显著且未识别——surface flip（引用集变化静默改变公共 API 面）、Option Strict 分叉、partial/生成器顺序契约、两遍绑定性能；Drawbacks 只列三项，未覆盖主要风险 | 已检查（预测待定） | 与生成器/partial 的顺序耦合未识别；"高度依赖分析器/生成器性能"自评被轻描淡写；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。材料=Anthony 13.4（正文未标章节号）；概念继承 VB6/VBScript `#Const`+`#If`（未标注）；`##` 记号非借鉴 C#（C# 无 `##`，C++ 的 `##` 是记号粘连，性质不同），属 Anthony 原创但未声明；sibling `#Ignore Warning` 依赖未标注（且该 sibling 已 Table） | 已检查 | "syntax-only"表述与材料实际影响（需探针绑定）有偏差；依赖关系未标注；无杂质但来源说明含糊 |

## 设计原则对照

- **与 VB 基因：偏离**——原则 #3（不引入第二种做事方式）直接冲突；原则 #7（避免隐蔽语义变化）因 surface flip 与"谓词求值依赖编译上下文"而不达标；`##` 不读如英语（#5）。仅原则 #9（消除样板）与动机挂钩，但样板是"符号维护"而非代码量，且 SDK 符号 + 漂移分析器可部分消解。
- **与主线关系：Anthony 独立延伸，且与主线方向张力**——主线把 `INotifyPropertyChanged` 样板定为源码生成器问题（2017-11-15，"poster child for the source generator feature"，原话），本建议把该场景拉回语言指令；主线 `#If` 是纯常量简单模型，本建议引入语义两阶段模型。与 `#Ignore Warning`（sibling，已 Table）耦合，其 RESOLUTION 要求本建议示例改两行式。非主线一致，也非明确冲突——是"方向张力的延伸"。
- **破坏性变更：新指令本身纯增量**（`##` 此前是词法错误）；但谓词翻转会静默改变声明面/重载面（同源码、不同引用集、不同 API 面），对消费方是"无源码改动的 breaking"；无 `langversion` 门控与警告策略。提案未做任何兼容性分析。

## 总评

- **达成程度：部分达成**——动机与缺口真实（API 存在性不可由版本符号表达），但设计未成熟：谓词文法、探针绑定、partial/生成器顺序、Option Strict 分叉全未定义；核心承诺（免语义分析）不成立；主要风险（surface flip、性能依赖）未分析。
- **LDM 三态建议：Consider**——不 Reject（缺口真实且主线未覆盖）；不 Active（新编译阶段级别的成本 + surface flip breaking 风险 + 证据不足）。若推进，以"扩展 `#If` + `TYPE_EXISTS`/`MEMBER_EXISTS`（裸标识符/字符串）+ 引用集钉定"为最小可行形态；`##` 记号、`METHOD_EXISTS(obj.Member)` 对象表达式、生成器样板用途分别 Reject / Table。
- **主要问题**：① `##` 构成第二种条件编译方式（原则 #3）；② 谓词文法（尤其方法组参数）与探针绑定模式未定义；③ "syntax-only"表述不成立；④ partial/生成器顺序与两遍绑定未设计；⑤ surface flip breaking 风险与 Option Strict 分叉未分析；⑥ 性能依赖自认未量化；⑦ 生成器样板场景与 2017-11-15 主线决定张力。

## 返工建议

- **补充章节**：Grammar（`#If` 扩展的谓词 BNF，或 `##` 文法——两者必须二选一并给出理由，建议前者）；Compatibility / breaking-change（surface flip、引用集钉定、可复现性、`langversion` 门控、警告策略）；Option Strict 分叉；与 `#If` / `#Disable Warning` / `#Warning` / `#Error` / 源码生成器的交互矩阵。
- **补充证据**：两阶段流水线最小原型（先组装编译、探针求值、丢弃分支、全量重绑定）及增量/后台编译性能基线；跨目标共享代码/链接文件痛点的量化数据；partial/生成器顺序的最小验证（谁先看到谁的声明）。
- **未决问题处理**：`METHOD_EXISTS(obj.Member)` 改为字符串形态或删除（方法组非 VB 合法表达式）；`##` vs `#If` 扩展二选一（倾向 `#If` 扩展，原则 #3）；`#End If`（单 `#`）笔误修正为 `##End If`；示例 `#Ignore Warning BC40008` 改两行式；移除 `...` 占位符；明确"类型存在性"的解析视图（引用集 + 当前编译 + 类型转发规则）。
- **设计探索**：把"生成器样板"场景移回生成器工作项（对齐 2017-11-15）；评估 PROPOSAL C——SDK 派生符号 + "符号漂移检测"分析器，作为对根因（符号与 API 脱节）的零语言表面替代；若坚持语义谓词，先论证"为何生成器语义 API 不够"，再讨论语言形态。

---

## 附录：C# 生态与互操作考量

> 范围说明：本提案（语义预处理 / 条件编译的语义化）是**纯编译期**特性，直接 CLR 元数据/IL 互操作面很薄（C# `#if` 与本提案的死分支都在编译期烘焙，不产生 IL 足迹差异）。真正的互操作维度在**工具链语义**上——C# 的预处理指令刻意保持词法级、C# 用"源码生成器 + 扩展 partial 方法"作为按 API 存在性出代码的正规出口。本附录据此对照，不硬凑元数据层面的互操作。基于索引 T6、T5、M5、M8，并在 `..\..\csharplang` 内 Grep 核实原文。

### 相关 C# 现实方向

**1. C# 预处理指令：词法级、符号驱动、刻意保持"浅"。** C# 的 `#if`/`#define`/`#undef`/`#elif`/`#else`/`#endif` 属词法结构的 §6.5 pre-processing directives，条件在词法阶段按 **conditional compilation symbols**（`#define` 的预处理符号）求值，不是对类型/成员的语义查询。LDM 在讨论 top-level 程序与 `#r`/`#load` 指令时明确把"把指令做深"当作要避开的悬崖：

> "At the extreme end, these directives could be nested inside `#if` directives, which starts to necessitate a true preprocessing step that the SDK tooling will need to understand and perform." → `meetings\2021\LDM-2021-05-12.md`（Simple C# programs）

> "Today, preprocessor directives in C# don't have massive effects, and they can be processed in line with lexing." → `meetings\2021\LDM-2021-05-12.md`（Simple C# programs）

预处理符号与语义实体是**两套命名空间**——`nameof` 明确排除预处理符号：

> "Labels and preprocessor symbols are not allowed in a nameof expression." → `meetings\2014\LDM-2014-07-09.md`（nameof）

C# 14 对指令的演进仍是工具/配置向，且保留符号模型：`ignored-directives` 新增 `#:`（shebang `#!` 之外的工具指令前缀，语言忽略），其限制条款显式依赖"条件编译符号"的既有机制：

> "Add `#:` directive prefix to be used by tooling, but ignored by the language." → `proposals\csharp-14.0\ignored-directives.md`（Summary）

> "Ignored directives must also occur before any `#if` directives because the tooling might not know the full set of conditional compilation symbols while parsing ignored directives." → `proposals\csharp-14.0\ignored-directives.md`（Restrictions）

**2. `#nullable` 是 C# 唯一"指令影响语义上下文"的先例，但仍是词法状态开关，不是语义谓词。** C# 8 的 `#nullable enable/disable/restore` 在源码内控制 nullable 注解/警告上下文：

> "The `#nullable` directive controls the annotation and warning contexts within the source text, and take precedence over the project-level settings." → `proposals\csharp-9.0\nullable-reference-types-specification.md`（§The `#nullable` directive）

它的机制是**设置一个模式**（词法阶段应用、影响后续绑定的上下文状态），而不是**查询编译内容**（"某类型/成员是否存在"）。语义谓词比它深一个量级——这正是本提案 `##If` 与 C# 任何既有指令的根本差异。

**3. C# 对"按 API 存在性出代码"的正规出口：源码生成器 + 扩展 partial 方法。** C# 9 版本历史把 Source Generators 列为正式 feature，C# 10 为 Incremental source generators（`Language-Version-History.md` 第 C# 9.0 / C# 10.0 两节）。语言侧配套是 C# 9 的扩展 partial 方法——它精确对应本提案"成员缺省"的意图：partial 方法在 **definition 缺失时擦除调用**，且专门为生成器场景开放签名限制：

> "This proposal aims to remove all restrictions around the signatures of `partial` methods in C#. The goal being to expand the set of scenarios in which these methods can work with source generators as well as being a more general declaration form for C# methods." → `proposals\csharp-9.0\extending-partial-methods.md`（Summary）

> "One behavior of `partial` methods is that when the definition is absent then the language will simply erase any calls to the `partial` method." → `proposals\csharp-9.0\extending-partial-methods.md`（Motivation）

> "This would expand the set of generator scenarios that `partial` methods could participate in and hence link in nicely with our source generators feature." → `proposals\csharp-9.0\extending-partial-methods.md`（Motivation）

**4. csharplang 无语义化条件编译提案。** 在 `..\..\csharplang\proposals` 内 Grep `TypeExists` / `MEMBER_EXISTS` / `semantic pre-processing` 均无命中；C# 侧没有 `#if TYPE_EXISTS(...)` 之类的先例。唯一与 `##` 相关的痕迹是 C# 14 `ignored-directives` 的 Alternatives 里考虑过 `##` 前缀（`##package ...`）作为"被忽略指令"的 sigil，最终弃用、改为 `#:`——`##` 在 C# 生态被评估过并被否决。

### 现实 vs 提案

| 维度 | C# 现实 | 本提案 | 判定 |
|---|---|---|---|
| 预处理指令模型 | `#if` 词法级、符号驱动，LDM 把 "true preprocessing step" 视为要避开的悬崖 | `##If` 语义谓词 = 两阶段流水线 + 探针绑定，正是那道悬崖 | **脱节（分叉）**：VB 侧"语义化延伸"在 C# 生态无对应先例，且 C# 刻意不把指令做深；`#nullable` 只证明"指令可影响语义上下文"，不证明"指令可查询编译内容" |
| "按 API 存在性出代码" | 源码生成器 + 扩展 partial 方法（definition 缺失即擦除调用） | `##If Not MEMBER_EXISTS(...)` 把探测搬回语言指令 | **冲突**：与本提案对"生成器样板"的用途正面撞车；C# 的态度是"语言给生成器留钩子，探测在生成器代码里做" |
| "成员缺省"原语 | partial method erase-on-absent | `##If` 分支丢弃 | **需桥接**：C# 已有"按成员有无实现"的语言原语，且面向同一需求；若未来语义条件编译，应优先对齐 partial 语义而非新指令族 |
| surface flip / 确定性 | 无语言内谓词翻转；目标面差异放在项目级（TFM / SDK 派生符号） | 谓词翻转静默改变声明面/重载面 | **需桥接**：跨语言维度放大——.vbx 产出面翻转时，C# 消费方在无源码改动下看到重载决议变化，"引用集快照"从建议升为强制 |
| CLR 元数据/IL | 死分支不产生树、不产生 IL | 丢弃分支不进语义模型、不进 IL | **兼容（薄）**：两边都是编译期烘焙，无元数据足迹差异；互操作面不在 IL，而在工具链语义与源码级条件表达 |

### 对 VBScript.NET 的适应建议

- **默认安全 / 按需语义**：`.vbx` 保留 `#If` 符号模型为默认（词法、便宜、与 C# 生态同构）；语义谓词仅作显式、`langversion` 门控的 opt-in——即本会议 RESOLUTION #2 的最小可行形态（扩展 `#If` + `TypeExists` / `MemberExists`）。不引入 `##`：`##` 在 C# 生态被评估后弃用（`ignored-directives` Alternatives §Sigil），读起来也不符合原则 #5。
- **source-gen 桥**：把"API 存在性探测"做成 Roslyn 增量生成器（`Compilation.GetTypeByMetadataName` / `ISymbol.GetMembers`），对齐 C# 生态 T6 的默认路径——这正是本会议 PROPOSAL C，现在获得生态级佐证：生成器与 C# 消费方共享同一语义模型，语言零表面，且"缺什么补什么"留在生成器代码里，`.vbx` 面向 .NET 生态的"自适应"走生成器，面向 VB 传统共享代码的窄角落再谈语言谓词。
- **识别新元数据 / 新 C# 产物**：C# `#if` 编译期擦除 → 消费 C# 程序集时无 `#if` 残留可辨，无需特殊处理。但 C# 9 扩展 partial 方法 + 生成器产物在元数据里是普通成员（生成器补齐的实现、`[CompilerFeatureRequired]` 等标志），`.vbx` 编译器按既有规则消费即可——前提是 `.vbx` 的生成器管线能与 C# 生成器共享同一 Roslyn 编译构造（"谁先看到谁的声明"的 partial/生成器顺序契约，本会议 OPEN QUESTIONS 已列）。C# 13/15 的 ref struct 等新特性标志识别属决策文件 M4/M8 范畴，非本提案专属。
- **跨语言面稳定性**：`.vbx` 产出若被 C# 消费，谓词翻转 = C# 侧"无源码改动 breaking"。spec 必须定义"引用集快照"作为谓词求值的稳定输入（本会议 RESOLUTION #6），并保证确定性（同源码 + 同引用集 → 同输出）；跨语言场景下这条从"建议"升为"强制"。

### 对既有 RESOLUTION / 三态判定的影响

C# 生态现实**不变更**本会议判定，但为三条结论提供**生态级佐证**：

- **RESOLUTION #2（`##` Reject）**：C# 无 `##` 记号，且 C# 14 在 `ignored-directives` Alternatives 里评估 `##` 前缀后弃用、改选 `#:`；C# 的指令演进方向是"词法级工具指令"而非"语义指令"。佐证"第二个指令族 / 非惯用记号"的判断在跨语言生态同样成立。
- **RESOLUTION #4（生成器样板 Table）**：C# 用"扩展 partial 方法 + source generator"覆盖同一场景（擦除缺省调用、让生成器补齐），与本会议引用的 2017-11-15 VB 主线决定方向一致。佐证"样板归生成器"是 .NET 生态共识而非 VB 独断。
- **三态 Consider**：C# 生态同样存在"手写共享代码按 API 存在性自适应"的空缺——C# 没有 `#if TYPE_EXISTS`，只能写生成器或手维护符号。佐证"缺口真实且未被主线覆盖"是**跨语言成立**的，不降级为 Reject；同时 C# 无语义预处理先例、有生成器替代路径、跨语言 surface flip 风险放大——也不构成升级为 Active 的理由。维持 Consider，续判信号不变（原型性能数据 / 痛点量化 / partial-生成器顺序规则）。

### 引用纪律与 OPEN QUESTIONS

本附录引用的 C# 原文均为 `..\..\csharplang` 内逐字核对，来源标注如上（`→ proposals\csharp-9.0\extending-partial-methods.md` 等）。另引用索引 T6/T5/M5/M8 的现状判断作为背景，非逐字引用。

- `OPEN QUESTIONS`：csharplang 历史上是否讨论过"语义化条件编译 / `#if TYPE_EXISTS`"——本附录 Grep `proposals` 无命中，但未穷尽全部历史 LDM notes 与 closed issues，需更全面检索确认。
- `OPEN QUESTIONS`：C# 标准 §6.5.3 pre-processing expressions 的精确文法（"条件编译表达式"除符号外是否允许字面量/运算符组合）——`spec\lexical-structure.md` 在库内仅是指针索引，正文已迁至 dotnet/csharpstandard，本附录未逐字核对库外正文。
- `Suspect`：`#nullable` 状态在 Roslyn 中于词法/解析阶段精确应用的实现边界——本附录仅据 spec 描述（"controls the annotation and warning contexts within the source text"），未核对 Roslyn 实现代码。
