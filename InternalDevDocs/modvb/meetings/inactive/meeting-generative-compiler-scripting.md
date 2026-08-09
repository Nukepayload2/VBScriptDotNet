# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。今天的议题来自建议目录的 **inactive** 子目录——`proposal-generative-compiler-scripting.md`（Anthony 原文第 18 章 18.18 "Generative compiler scripting"）。这是整个建议目录里最"空"的一份：全文没有动机、没有语法、没有示例，唯一的内容是 Anthony 对自己这个想法的一句话判断：

> "I'm pretty sure this is a bad idea. But at some point I'll venture into that dragon's lair."（原文 18.18，逐字）

按 vblang 的建议生命周期，inactive 意味着"有合理前景但目前不排期，复活是被允许的"。所以我们今天的任务不是评审一个设计——**那里没有设计可评审**——而是回答两个更基础的问题：**第一**，"生成式编译器脚本"这个词到底可能指什么，它对我们意味着什么；**第二**，值不值得为它启动设计，还是继续留在龙穴门口。我们开场就知道这场会不会轻松通过，因为我们对"编译期运行用户代码"这件事有充分的、来自主线先例的警惕。

## Agenda

* [Proposal: 生成式编译器脚本（Generative Compiler Scripting）](#proposal-生成式编译器脚本generative-compiler-scripting)

## Proposal: 生成式编译器脚本（Generative Compiler Scripting）

_Related: [vblang #211 – Discussion / proposal: usage of any installed programming language](https://github.com/dotnet/vblang/issues/211)；[vblang #107 – Replaceable Members](https://github.com/dotnet/vblang/issues/107)；ModVB：`proposal-semantic-preprocessing.md`（`##If`）、`proposal-replacement-modifiers.md`（`Replaces`）、`proposal-partial-members.md`、`proposal-scripting-interpreted.md`（脚本与解释执行，同为 inactive）_

### 场景与缺口

We started by trying to pin down what the phrase even means, because the proposal itself lists four candidate readings and declines to choose one：**改写 IR？注入代码？自定义编译流水线？运行时机/宿主/权限模型？** 原文 18.18 只有一句自评，没有任何展开。这不是"设计尚未定稿"，而是"概念尚未定义"。

我们把"生成式编译器脚本"放在一片我们都很熟悉的地图上：开发者想要**影响编译过程本身**——不只是声明性地请求生成代码，而是用代码去读取、改写、驱动那个正在运行的编译器。这个欲望是真实的，而且它有一个完整的、由近及远的谱系：

```vb
' 谱系的最远端（我们自造的草图，不是 VB 语法，也不来自 Anthony）：
' 一个"编译器脚本"，在编译进程内读取语法树、改写 IR、接管流水线。
Script CompileScript
    For Each tree In Compilation.SyntaxTrees
        If tree.ToString.Contains("TODO") Then
            tree = tree.WithRoot(Rewrite(tree.GetRoot()))   ' 改写语法树?
        End If
    Next
    Compilation = Compilation.WithEmitOptions(...)           ' 接管代码生成?
End Script
```

谱系的近端是我们已经拥有或正在推进的**受控机制**：分析器（只报告，不改写）、源生成器（在独立进程运行，以声明为输入、以生成代码为输出）、语义预处理 `##If`（Anthony 的另一份建议，声明式谓词）、`Replaces` 替换修饰符（Anthony 的系统化）。**这些受控机制覆盖了谱系上绝大多数"编译期智能"的渴望；本建议是谱系上唯一不受控的一端——"编译器里跑任意脚本"。**

所以场景与缺口并不来自"用户今天做不了什么"——我们 `Suspect` 缺口主要是概念性的：Anthony 想做某种"编译期可编程性"，但他自己也觉得这是坏主意，且没有说出任何一个用受控机制做不到的具体场景。

### 候选方案

**PROPOSAL A — 完整生成式编译器脚本（Anthony 的"龙穴"）。** 按建议标题的最强读法：允许以脚本形式参与、驱动、改写编译过程——脚本在编译进程内（或进程外但以通用语言）运行，能访问语法树/语义模型/IR/代码生成，能改变编译产物。这是 `proposal-semantic-preprocessing.md` 明确定义过关系的概念空间的最远端："高度依赖某些分析器或源码生成器技术的性能"——而脚本化把这个依赖推到顶点。

**PROPOSAL B — 受限的编译期扩展点。** 不做通用脚本，只在编译器里开几个**有界、确定、沙箱化**的扩展点（例如"绑定前"、"生成前"两个阶段，插件以受限 API 访问数据、产出增量结果，禁止 IO/网络/非终止循环）。这是"编译期可编程性"与"编译器可信边界"的折中——但它实质上重新发明源生成器，且更不安全。

**PROPOSAL C — 不做编译器脚本，依靠受控机制。** 明确宣布本建议所指的空间已被四件受控工具占领：源生成器（改写/注入代码）、分析器（只读诊断）、`##If`（声明式语义条件）、`Replaces`（声明式替换）。编译器本身作为**库**（compiler-as-a-service）已经是可以被外部脚本调用的 API——"生成式"不需要进入语言，只需要留在库层。

**PROPOSAL D — 什么都不做，保持 inactive。** 尊重 Anthony 自己的判断：他说这是坏主意。没有动机、没有语法、没有用户请求，`Suspect` 唯一的价值是"把想法记下来，等作者某天进龙穴时有个坐标"。

### 权衡：Q&A

- **A 的吸引力到底在哪？** 全部吸引力是"无限能力"——如果编译期的每一步都能被脚本改写，语言就不再有"表达不出的元程序"。但我们在 2017-12-06 已经对同族问题做过结论，主线原话："We absolutely cannot rush full meta-programming solution and our plate is pretty full already with HUGE ticket items for the next major version that generators are extremely unlikely to fit in either developer-time or calendar-time."——那是对**源生成器**（受控的那个）说的；A 是它的无限版，成本只高不低。
- **B 是不是 A 的可救赎版本？** `Probably` 不是。B 把"脚本"缩成"有界插件"，但"有界"恰恰抹掉了 A 的全部卖点（任意改写）。而一旦有界，它又回到 C 的受控机制——源生成器已经是有界扩展点的成熟形态，且有独立进程隔离。B 两头不讨好：对追求权力的人太弱，对维护可信边界的人太强。
- **C 会不会太保守？** 不会——C 不是"什么都不给"，而是"给声明式的、可审计的、确定的东西"。我们 2018-03-21 看过 Anthony 用属性做编译期重写的演示，主线记录："We spent the rest of the meeting with Anthony's demo for affecting behavior using attributes and compile time rewriting. Overall impression was positive."。**编译期行为修改本身是被欢迎的；问题是它要"声明式属性"还是要"任意脚本"。我们喜欢前者。**
- **D 与"将来进龙穴"矛盾吗？** 不矛盾。D 不是永久否定，而是"现在不做、记下来、给激活信号"。Anthony 自己说"at some point I'll venture into that dragon's lair"——我们替他守门，条件不成熟就不放行。
- **和"脚本方言"（`proposal-scripting-interpreted.md`）是不是一回事？** 不是，必须分开。脚本方言是把 VB 作为语言拿去解释执行（原文对那条建议的回应是 "Of course."）；本建议是"在编译器内部跑脚本"。2017-12-06 对顶层语句（#102）讨论过 C# 的脚本方言——"Today they use the scripting dialect of C# by virtue of using the Scripting API"——那是运行时的可交互性，与本建议的**编译期可信边界**问题没有共享的实现面。**我们明确反对把两份建议当成彼此的铺垫。**
- **有没有可能我们过度解读了"脚本"？** 有可能。`Suspect`：Anthony 那句话更像是"某天想做点编译器内可编程的东西"的随意表态，而不是一份特性请求。我们把这列为 OPEN QUESTION。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

**没有语法可查——这是本建议最诚实也最致命的特征。** `Script`/`End Script` 不存在，指令不存在，属性不存在，独立文件类型不存在。当我们问"编译器脚本长什么样"时，候选形态本身就是未定的：是源文件里的指令？是独立的 `.vbs`-like 文件？是特性标记？是 IDE 菜单动作？每一种形态都会打开不同的文法问题（脚本与普通代码如何区分、脚本边界在哪、脚本是否参与自身所在编译的语义），而建议对所有这些都一字未提。语法歧义在"没有语法"面前无从谈起——但也正因为没有语法，我们无法进入任何有意义的文法讨论。

#### 2. 角案例与边界语义

即使只谈概念，角案例也足够把我们劝退：

```vb
' 我们自造的草图（非 VB 语法）。编译期脚本的非终止 = 构建拒绝服务。
Script CompileScript
    Do
        ' 编译器没有"编译超时"概念。这个循环挂起整条构建。
    Loop
End Script

' 同一草图：脚本依赖环境，编译器不再是"输入 → 输出"的纯函数。
Script CompileScript
    If Directory.Exists("C:\flags\optimize.txt") Then
        Compilation.EnableOptimization()   ' 同一源码，两台机器编译出不同程序集
    End If
End Script
```

- **非终止与资源耗尽**：编译期脚本一旦死循环，构建没有天然的"编译超时"纪律可以打断它——这是拒绝服务，不是编程练习。
- **环境依赖**：脚本能读文件、环境变量、网络，则 `/deterministic` 构建、增量编译缓存、并行构建全部失效——"编译器是输入的纯函数"这一不变量被打破。
- **异常**：脚本抛异常时编译怎么办？回滚？部分产物？诊断里怎么报"编译期脚本崩了"？
- **重入**：脚本读到的语法树/语义模型是"正在被编译的那个程序"——编译器对自己半成品的读取是重入的，语义模型会返回"还不存在"的符号。
- **多目标（TFM）**：一个项目多 TFM，脚本每次运行结果不同怎么办？

这些没有一个是我们"再想想就能解决"的细节，而是"需要重新设计编译器的安全模型"的结构性问题。

#### 3. 作用域与绑定

脚本能看见什么？这决定了它的能力也决定了它的危险。若脚本可访问语义模型，它绑定到的符号是**编译进行中的、未固定的符号**——这要求编译器暴露一个自指的 Roslyn API 面。我们 2014 年对 `nameof` 等特性讨论过语义模型的稳定性问题（编译期查询必须可复现、可缓存）；任意脚本把这条线踩平了。`Probably`：任何实现都要把脚本限定到"与语义模型只读交互、产出增量语法片段"——而那已经不是"编译器脚本"，是"换皮源生成器"。

#### 4. 与既有特性的交互

这里有一个**已存在的完整替代生态**，我们逐件对照：

- **源生成器**（C# 已落地，主线 2017-11-15 视其为 `INotifyPropertyChanged` 的"poster child"）：做"注入代码/生成样板"，独立进程隔离。→ 覆盖"注入代码"读法。
- **分析器**：只读诊断，不生成代码。→ 覆盖"检查代码"读法。
- **`##If` 语义预处理**（`proposal-semantic-preprocessing.md`）：声明式谓词 `METHOD_EXISTS`/`TYPE_EXISTS`/`MEMBER_EXISTS`，有界、确定、可读。→ 覆盖"按语义条件编译"读法。
- **`Replaces` 替换修饰符 + `Partial Members`**：声明式替换方法体。→ 覆盖"局部改写"读法。
- **编译器即库**（Roslyn API）：任何外部脚本今天就能调用 `Compilation` API 做生成与重写——不需要语言表面。

**结论：四件受控工具 + 库层 API 已经把"生成式"的需求拆光了。** 编译器脚本若落地，就是 2018-06-13 明说的"第二种做事方式"，而那次会议对这个门槛的原话是："our bar for expansion of the surface area - making a second way to do things - will be relatively high even when it's a good idea"。这甚至还不是一个被确认的 good idea。

#### 5. Breaking change 与兼容性

**当前无破坏——没有语法，就没有"旧代码重解释"。** 但我们把隐患标注清楚：若某天真的引入，破坏的不是单个语法，而是**编译器行为的不变量**——确定性、缓存键、并行构建、可信边界。2018-06-13："We will almost never make breaking changes to Visual Basic"。A 是在"几乎从不破坏"的底线上下赌注。任何实现都必然需要 `langversion` 门控、沙箱进程、确定性审计——而这些全是编译器基础设施改造，不是语言特性。

#### 6. Option Strict / 编译选项分叉

严格/宽松两条路径的一致性义务，在本建议下变成更重的要求：**脚本在两种 Option、两种编译模式（增量/全量、单/多进程）、两种宿主（命令行/IDE）下必须逐字等价。** `Probably` 不可能实现——脚本能看到 `Option` 并据此改变行为，等于给了它一条天然的"分叉指令"。我们对"编译器脚本能读编译选项"这种能力保持明确的拒绝态度。至于 `##If` 这类声明式机制，两条路径天然一致（谓词求值不依赖宿主），这正是我们偏爱它的理由之一。

#### 7. IDE / IntelliSense

**这是决定性的追问。** IDE 里的实时分析在**每次按键**时运行编译器。若"编译器脚本"存在，它每次按键都会被运行——在编译进程内运行用户作者的任意代码，**每个按键一次**。这同时是性能灾难与信任灾难：IDE 会因用户的脚本死循环而卡死，恶意/错误脚本能访问 IDE 进程的全部能力。诊断与补全都要回答"编译期脚本现在算出了什么"，而脚本自身在 IDE 里如何断点调试、如何编辑（它是源文件还是配置？）全是未解问题。我们见过分析器/生成器的 IDE 化要付出多少——`##If` 的语义检查已经在警告 "高度依赖分析器/源生成器技术的性能"；脚本化把这个性能与信任负担推到极限。

#### 8. 数据 / 普遍性

**没有数据，没有用户请求，没有场景陈述。** 对比我们评价体系里的分水岭（Implicit-default-optional 有 85% 统计支撑），本建议连"5% 场景"的证据都没有——它连"场景"二字都没有。主线 2017-12-06 对源生成器的判断是"extremely unlikely to fit in either developer-time or calendar-time"，那还是在有人提出具体 INotifyPropertyChanged 痛点的前提下；本建议没有提出任何具体痛点。`Suspect`：真实的渴望（元编程）存在，但已被受控机制服务；剩余"不受控的渴望"没有量化证据。

#### 9. 更简替代

逐条列出（详见 #4）：源生成器、分析器、`##If`、`Replaces`、Roslyn 库 API、MSBuild 任务。**它们不仅更简，而且已经存在或已在推进。** 建议自己的 Alternatives 也承认："用源生成器 / 分析器在受控的进程中实现同等能力，相对更安全。" 我们在它自己的 Alternatives 之上再加一条：**"编译器即库"已经把"生成式"放到库层——生成不需要进入语言。** 2018-02-07 对 #211（"使用任何已安装语言"）的态度可作注脚："Fantastic idea, and too hard to do."——我们对"编译器脚本"想说的是同一句话，只是连"Fantastic"都还够不上。

#### 10. 复杂度 / 成本 / 优先级

#211 的主线评估是 "It would require rewriting considerable parts of Roslyn."——那还只是"多语言混合"；本建议是"编译器可被任意脚本改写"，是同一量级的架构改动。成本：编译器安全模型 + 沙箱/独立进程 + 确定性/缓存重建 + IDE 集成 + 调试体验，五块重活，全无现成答案。优先级：**目录最低**。它排在源生成器、`##If`、`Replaces` 之后——后三者是"编译期智能"的当下正解，本建议是它们的反面。我们认同 2017-12-06 的结论结构：绝不 rush 完整元编程方案。

#### 11. 运行时 / CLR 硬约束

脚本产出的 IL 本身不触达 PEVerify（它仍是普通 IL），但**运行脚本的宿主**触达更底层的问题：加载/卸载脚本程序集（`AssemblyLoadContext` 可卸载域）、进程隔离、信任边界（脚本能访问编译器进程的磁盘/网络/环境变量）、并行构建下共享缓存的数据竞争。CLR 没有给"编译器内跑任意代码"提供内置安全网——沙箱要靠编译器自己搭。这不是"某条 CLR 规则限制我们"，而是"CLR 把安全责任完全留给了我们"。

#### 12. 值不值得做

逐维打分。**价值**：未定义——没有场景，无法计分；受控机制已覆盖大部分。**成本**：极高——"rewriting considerable parts of Roslyn" 量级，且是安全/确定性/IDE 五线作战。**风险**：高——可信边界、确定性、IDE 每次按键执行用户代码、构建拒绝服务。**结论：现在不值得做，且在不满足激活信号前不值得再讨论。** 这不是"Fantastic idea, too hard to do"的热情惋惜，而是"idea 未成形、作者自评坏主意、主线两度评估过高成本"的冷处理。

### VB 基因对照

- **永不破坏既有代码（原则 #1）**：无语法故无直接破坏；但若落地，破坏的是编译器行为不变量（确定性、缓存、并行），比破坏某个语法更伤。
- **保持 VB-like / 读起来像英语（原则 #2、#5）**：任意脚本既不像 VB，也不像英语；它是一段看不见的、改变产物语义的程序。VB 对"声明式、可读"的偏爱在 `##If`（"If this method exists then..."）里得到了满足，在 `Script CompileScript` 里完全丧失。
- **不引入"第二种做事方式"（原则 #3）**：**重罚项。** 源生成器 + 分析器 + `##If` + `Replaces` 已组成"编译期行为"的第一种方式；编译器脚本是第二种，且是 2018-06-13 定义的高门槛类型："making a second way to do things - will be relatively high even when it's a good idea"。
- **不为边缘场景加特性（原则 #6）**：无数据、无用户请求、无场景——教科书级的"边缘中的边缘"。
- **避免隐蔽控制流/语义变化（原则 #7）**：`Return?` 因为"细微字符改变语义"被拒；编译器脚本是它的极端形态——**整个编译产物都被一段不可见代码改变语义**。
- **消除常见样板（原则 #9）**：目标正确，但实现面已被源生成器（"poster child" 是 INotifyPropertyChanged）与 `##If` 承担。
- **与主线关系（对照表 2.3）**：主线对"完整元编程"无正面方向——2017-12-06 判源生成器 "extremely unlikely to fit"、2018-02-07 判 #211 "Fantastic idea, and too hard to do"；2018-03-21 欢迎的是**属性驱动的编译期重写**（受控版），不是脚本。Anthony 18.18 是独立延伸，且自评坏主意。**方向与主线相反，作者本人也不背书。**

### RESOLUTION:

1. **保持 inactive（Table / LDM No Plans），不激活全貌（PROPOSAL A）。** 理由叠加：① 无动机、无语法、无示例、无用户请求——概念未定义，Anthony 自评 "I'm pretty sure this is a bad idea"；② 主线 2017-12-06 已判完整元编程 "absolutely cannot rush"、2018-02-07 判 #211 "too hard to do"，本建议是那两类评估的更弱版本；③ 四件受控机制（源生成器、分析器、`##If`、`Replaces`）+ 编译器即库已覆盖"生成式"诉求。**对 PROPOSAL A 的方向性姿态是 Reject**——"编译器内跑任意脚本"我们不打算在可预见的未来做。
2. **PROPOSAL C 是当前立场：编译期智能走受控机制。** 本建议的"代码注入/条件编译/局部改写"读法，分别由源生成器、`##If`、`Replaces` 承担；"生成式"留在库层（Roslyn API），不进语言。
3. **PROPOSAL B（有界插件）不单独立项**——它要么退化成源生成器（多余），要么漏出不可控面（危险）。
4. **明确区分两条线**："脚本方言 / 解释执行"（`proposal-scripting-interpreted.md`，"Of course."）与"编译器脚本"（本建议）是不同建议，互不暗示、互不依赖。
5. **激活所需信号（任一满足才重审）**：
   - (a) 出现一个**具体的、可编译的**场景，且证明源生成器/分析器/`##If`/`Replaces`/Roslyn 库 API 五者**都**无法表达——目前 `Suspect` 不存在，但以证据为准；
   - (b) 出现一份**原型**，证明有界、沙箱化、确定性、缓存安全的扩展点可行（独立进程、无 IO/网络/非终止、输入输出可作缓存键）——我们 `Probably` 做不到，但不关闭证明；
   - (c) 主线信号：C#/Roslyn 若采用通用编译器脚本机制，按 2018-02-07 可空性的先例——"We'll postpone this until we understand the uptake in C#"——重审跟随；
   - (d) Anthony 自己"进龙穴"时给出设计草图——龙穴需要地图才能进入，一句话自评不是地图。

### Implication:

- 将本建议状态标注更新为 **LDM No Plans（保持 inactive）**，并在建议文档头部注明本次会议、关联 #211 与 2017-12-06 源生成器评估。
- 建立"编译期可扩展性地图"（对标 postfix-casting 会议委派的"转换写法全景"备忘）：收录源生成器、分析器、`##If`、`Replaces`、Partial Members、编译器即库，以及本建议（作为该地图的最远端锚点）。任何新增"编译期行为"提案必须先读此地图。
- 给 `##If` 团队回执：语义预处理的**声明式、有界、确定性**正是本建议诉求的 VB 化答案；若 `##If` 未来要扩展谓词，它承接的正是"编译器脚本"想要的那部分安全子集。
- 跟踪两条气压计：C# 源生成器的采用率与工具成熟度（2018-02-07 "understand the uptake" 先例）；Anthony 18.18 是否出现续章。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：Anthony 的 "venture into that dragon's lair" 到底指什么——通用插件面？声明式编译期 DSL？还是随口一句？原文无任何线索。`Suspect`：指某种"编译器可编程性"的通用面，但纯属推测。
- `OPEN QUESTIONS`：`##If` 的用户自定义语义谓词是否足够承接"编译器脚本"的声明式子集——若是，本建议的可复活部分就整体并入 `##If`，无需新语法。
- `OPEN QUESTIONS`：若 C# 未来落地编译器插件/脚本机制，VB 的"默认跟随 C#"原则（2018-06-13 "C# will take the lead"）是否触发重审——我们倾向触发，但那属于"跟随已证实机制"，不是"带头冒险"。
- `TODO`：建"编译期可扩展性地图"备忘，收录五件受控工具 + 本建议。
- `TODO`：量化"样板代码需求"在源生成器/`##If`/`Replaces` 覆盖后的剩余缺口——这是激活信号 (a) 的反向证据。
- `Follow-up`：跟踪 Roslyn 源生成器的设计时支持（补全、调试、增量缓存）成熟度，作为受控路线的健康度指标。

### 状态

- **LDM 状态：No Plans（保持 inactive）**；PROPOSAL A 方向 Reject；受控机制路线（PROPOSAL C）即当前立场。
- **三态判定：Table** — 想法真实但未成形、作者自评坏主意、主线两度评估过高成本、受控替代已覆盖价值。值得记住（进地图），不值得现在做；绑定四条激活信号，任一满足才重审。

---

## 附录：特性评价

# 建议评价报告：proposal-generative-compiler-scripting.md

## 评价对象

- 建议：proposal-generative-compiler-scripting.md — 生成式编译器脚本（让开发者以脚本参与/驱动/改写编译过程）
- 来源：Anthony 原文第 18 章 18.18 "Generative compiler scripting"（`..\..\AnthonyDesign_wordpress.txt` L3070–3074；全文仅一句："I'm pretty sure this is a bad idea. But at some point I'll venture into that dragon's lair."）
- 配方目标：无——原文未给出任何动机、语法或设计；建议如实记录了"一句话态度"

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 1/5 | 锚点 1："目标定义不清"（无目标）。无动机、无语法、无示例、无原型；"生成式编译器脚本"指什么（改写 IR/注入代码/自定义流水线）未定；Anthony 自评"坏主意"。声称的效果为零 | 未提供 | 效果完全未显现；未决问题 ≥4 个关键设计点（改写 IR/注入/流水线/运行时机/权限）→ 效果证据等级封顶为未提供 |
| 特性 | 1/5 | 锚点 1："核心与 VB 基因冲突"。无任何 VB 基因可继承（无语法）；概念与"简单/低仪式/稳定/可信"基因冲突，是"隐蔽控制流"（原则 #7）的极端形态，且是"编译期行为"的第二种做法（原则 #3） | 未提供 | 无语法即无"继承"，方向性冲突即扣分；`Suspect` 若落地的 VB 化形态只会是声明式（即 `##If`），而非脚本 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊"。六章节齐全、如实声明原文仅一句、Drawbacks/Alternatives/Unresolved 实质性非空且诚实（3 个未决问题具体诚实，1–3 健康区间加分）；但 Detailed design 为空、无任何示例、状态行全部占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）、未与主线 2017-12-06 源生成器评估 / #211 对话 | 已提供/已检查 | 诚实不等于有内容；"空"是该建议的本质，但按六章节模板评审，核心设计章节缺失 |
| 属性 | 2/5 | 锚点 2："某关键维度受损且无应对"。暗风险突出且无任何缓解设计——可信边界（编译器进程内跑任意代码）、确定性（`/deterministic`/增量缓存失效）、并行构建、IDE 每次按键执行脚本、构建拒绝服务（非终止）；风（演化一致性）与受控机制路线断裂；唯一正向是"记录了想法"，不构成多维受益 | 已检查（预测待定） | 五处暗风险无一对冲设计；与语言当前"受控可扩展性"生命阶段背道而驰；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注；标注与影响有偏差"。来源标注准确（Anthony 18.18 一句逐字引用，无 C# 借鉴、无 VB6 继承、原创）；但**未标注**最相关的两件材料——主线 2017-12-06 源生成器评估（"absolutely cannot rush full meta-programming solution"）与 #211（"Fantastic idea, and too hard to do"）；与 `##If`/`Replaces`/源生成器的受控替代关系仅在 Alternatives 一句带过，未系统标注 | 已检查 | 成分本身只有一句原文；缺主线先例标注与受控替代谱系标注，影响预估方向单一（只列缺点，未列"谁已替代它"） |

## 设计原则对照

- **与 VB 基因：偏离（方向性）。** 无语法故无直接"违反"，但概念层与原则 #3（第二种做法）、#7（隐蔽控制流/语义变化）、#6（无数据边缘场景）、#1（稳定不变量）相悖；"声明式、可读、有界"的 VB 答案恰好是 `##If` 而非脚本。
- **与主线关系：Anthony 独立延伸，且与主线方向相反。** 主线对完整元编程无正面表态：2017-12-06 判源生成器 "extremely unlikely to fit in either developer-time or calendar-time"；2018-02-07 判 #211 "Fantastic idea, and too hard to do"；2018-03-21 欢迎的是**属性驱动的编译期重写**（受控版）。C#/Roslyn 亦无通用编译器脚本方向——本建议无 C# 牵引力。
- **破坏性变更：无**（无语法、无实现，无从破坏）；但若落地，潜在破坏编译器行为不变量（确定性、缓存、并行、可信边界），量级远超一般语法破坏。

## 总评

- **达成程度：未达成（作为可落地特性）/ 达成（作为记录在案的实验性想法）。** 建议诚实记录了 Anthony 的一句自评与"总有一天进龙穴"的表态——作为存档合格；作为语言特性，效果、特性、属性三维全部不达标，设计本体为空。
- **LDM 三态建议：Table（保持 inactive / LDM No Plans）**；PROPOSAL A 方向 Reject；面向 VBScript.NET 的优先级同样为 **Table**——脚本型项目（如 VBScript 迁移）反而依赖宽松类型与受控工具，编译器内跑任意脚本不服务迁移痛点，只引入可信边界风险。
- **主要问题**：① 无动机/无设计/无示例，概念未定义，Anthony 自评坏主意；② 主线已两度评估完整元编程为过高成本（2017-12-06、#211）；③ 受控替代（源生成器+分析器+`##If`+`Replaces`+编译器即库）已覆盖大部分价值；④ 可信边界/确定性/缓存/IDE 性能/拒绝服务风险无解；⑤ 与"脚本方言"建议（`proposal-scripting-interpreted.md`）易混淆，须明确分离。

## 返工建议

- **补充章节**：与受控机制逐场景对照表（源生成器/分析器/`##If`/`Replaces`/Roslyn 库 API vs 编译器脚本，证明存在五者都无法表达的缺口）；明确"脚本"的运行时机、宿主与权限模型；给出至少一个可编译的端到端示例。
- **补充证据**：用户请求数据；C#/Roslyn 侧的动向（源生成器采用率、设计时工具成熟度）；最小原型证明沙箱/独立进程/确定性/缓存安全四要素可同时成立。
- **未决问题处理**：与 `##If` 团队对表，把"声明式子集"整体划给 `##If`（用户自定义语义谓词），消除本建议的可复活部分；明确区分"脚本方言"（另一建议）与"编译器脚本"；PROPOSAL A 记录为方向 Reject 而非暂缓。
- **设计探索**：把本建议重新表述为"编译期可编程性的安全边界在哪里"——即用受控机制逐一回应"改写 IR/注入代码/自定义流水线"三个读法，得出"哪个读法已被替代、哪个读法不可安全实现"的裁决表；该表进入"编译期可扩展性地图"备忘。

---

## 附录：C# 生态与互操作考量

> 依据 `..\..\..\csharplang-index.md`（dotnet/csharplang 官方仓库 interop 浓缩索引；来源目录 `..\..\..\csharplang`）撰写，个别原文经 Grep 回 `..\..\..\csharplang` 核实。本提案主题（生成式编译器脚本——"编译器可被脚本化、可在编译期生成/改写代码"）在 C# 生态的最大现实摩擦点是索引 **T5（AOT/trimming/反射互操作）、T6（source generators/元编程）、M5（runtime-library/scripting-interpreted/generative-compiler-scripting ↔ source-gen/AOT）**：C# 已把"编译期生成代码"做成**受控机制**（source generators + incremental generators + experimental interceptors），并受 AOT/trimming 压力塑造为"把信息放回类型系统"；而"编译器里跑任意脚本"恰是该方向的**反面**。本附录给出「C# 现实方向 vs 本提案响应」对应，判断兼容/冲突/需桥接，并对 VBScript.NET 提出适应建议。

### 相关 C# 现实方向

1. **C# 把"编译期生成代码"做成受控机制，而不是任意脚本。** Source Generators（C# 9）+ Incremental Generators（C# 10）在独立进程中运行，以声明为输入、以生成代码为输出、缓存中间结果。C# 版 Language-Version-History 将其列为官方 feature，对 incremental generators 的逐字描述：「Incremental source generators: improve the source generation experience in large projects by breaking down the source generation pipeline and caching intermediate results.」→ `Language-Version-History.md`（C# 10 条目）。csharplang 仓库内**没有**"通用编译器脚本 / 编译器插件"方向——最接近的受控提案仍是 `partial` 扩展，其 Summary 原文：「The goal being to expand the set of scenarios in which these methods can work with source generators as well as being a more general declaration form for C# methods.」→ `proposals\csharp-9.0\extending-partial-methods.md`（Summary）。`Suspect`：source generators 的**规范正文不在 csharplang**（特性文档在 dotnet/roslyn 的 `docs/features/incremental-generators.md` 与 C# 9 官方博客，Language-Version-History 仅外链）——精确引用需到 dotnet/roslyn 深挖，本附录不臆造。
2. **AOT/trimming 是压倒性生态压力，方向是"把信息放回类型系统"而非类型系统外 hack。** LDM-2023-02-27 对 interceptors 的动机原文：「This feature is intended to help make code AOT-friendly by allowing source generation to replace runtime reflection.」→ `meetings\2023\LDM-2023-02-27.md`；LDM-2023-07-24 继续：「This approach is necessitated for all the reflection-based scenarios that use information that exists outside the type system to affect runtime code; because these scenarios use information not statically available during compilation, it is hard to make them AOT-compatible.」以及「we think that we need to take another look at the scenarios that are considering interceptors and see if we can put that information back into the type system」→ `meetings\2023\LDM-2023-07-24.md`。方向本质（索引 T5）：**类型系统承担更多本来靠反射/动态完成的职责**（unions、closed hierarchies、source-gen、interceptors），以服务 NativeAOT 与 trimming。
3. **interceptors 是"source-gen 的实现细节"，不是通用脚本面。** IC-2023-04-04 原文：「We want them to be an implementation detail of certain source generators.」→ `meetings\working-groups\interceptors\IC-2023-04-04.md`；其依赖 `[Experimental]` 实验特性（→ `proposals\csharp-12.0\experimental-attribute.md`）。且 LDM-2023-02-27 明确记录语言互操作的顾虑：「As a C#-only feature, it could negatively affect the F# and VB ecosystems.」→ `meetings\2023\LDM-2023-02-27.md`。
4. **动态 / 表达式树 / 晚期绑定相对边缘化。** `dynamic`（C# 4）与 Expression trees（C# 3）长期无大演进；unsafe-evolution 的 open question 甚至质疑 dynamic 在 AOT 下是否应标 unsafe（索引 T7）。方向本质：**动态不是 C# 的前进方向，C# 靠类型系统与 source-gen 取代之。**
5. **unsafe-evolution 对 VB 的明确表态（元数据桥接义务）。** 原文：「We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either.」→ `proposals\unsafe-evolution.md`（「VB」小节）。但 .NET 11 后 C# 指针会更多地在"非 unsafe 上下文"出现、成员会标 requires-unsafe——VB/VBScript.NET 编译器必须**认识这些新元数据**（`RequiresUnsafeAttribute`/`MemorySafetyRulesAttribute`）才能正确校验跨语言调用（决策文件 M8）。

### 现实 vs 提案

| 本提案读法 | C# 现实方向 | 判定 |
|---|---|---|
| **PROPOSAL A**：编译器内跑任意脚本（改写 IR / 注入代码 / 接管流水线） | C# 无此方向；受控 source-gen + "把信息放回类型系统" + AOT 静态推理；任意脚本破坏确定性、缓存键、并行构建、可信边界 | **冲突** |
| **PROPOSAL C**：依靠受控机制（源生成器/分析器/`##If`/`Replaces`/编译器即库） | 与 C# source generators + incremental generators + `partial` 扩展 + compiler-as-a-service（Roslyn API）**高度同构**；连最"激进的" experimental interceptors 都被定位为 source-gen 实现细节 | **兼容**（本提案 RESOLUTION 的立场恰是 C# 现实方向） |
| **亲属建议**：`proposal-scripting-interpreted.md`（脚本方言/解释执行） | 解释执行=运行时反射、`DynamicMethod`、运行时程序集加载——与 NativeAOT/trimming 天然冲突（决策文件 M5） | **需桥接** |
| **PROPOSAL B**：有界沙箱化编译器插件 | C# 未选择"通用插件面"；现有扩展点（source-gen/analyzer）已是有界+独立进程的成熟形态，B 是它们的更不安全版本 | **冲突（多余）** |

理由要点：

- **A 与 C# 的根本分歧不在"想不想生成代码"，而在"代码生成发生在哪个可信边界内"。** C# 把生成器放在**编译器的独立进程之外**、以声明为输入；本提案 A 把它放进**编译进程之内**、以"正在编译的程序"为输入——后者的重入、环境依赖、非终止与 C# 的增量缓存/`/deterministic` 基建直接冲突。这正是本提案正文 2017-12-06 判源生成器 "extremely unlikely to fit" 的同一原因，只是 A 把代价推得更高。
- **C# 对"类型系统外 hack"的总体态度是收缩而非扩张**：interceptors 本是为了缓解 AOT 反射难题，但 LDM 立刻转向"看看能否把信息放回类型系统"（→ `meetings\2023\LDM-2023-07-24.md`）。"编译器里跑任意脚本"是"类型系统外 hack"的极限形态，与这条主线相反。
- **"C# 变体 / 只做兼容"的 VB 主线压力**：LDM-2023-02-27 已担心 interceptors 这类 C#-only 特性 "could negatively affect the F# and VB ecosystems"（→ `meetings\2023\LDM-2023-02-27.md`）。VBScript.NET 若推出 C# 没有的"编译期脚本"面，等于主动制造**第二套元数据/行为约定**，与"跟随 C# 元数据即可互操作"的默认路线背道而驰。

### 对 VBScript.NET 的适应建议

1. **默认安全 / 按需动态（双模路线）。** 编译期脚本化（A）不做；解释执行/晚期绑定（`Any`、脚本方言）保留给 COM/Office 这类 VBScript 传统强项场景，但定位为**显式 opt-in 的传统兼容层**。默认路径是"编译到受管程序集"，让 .vbx 产物可进 AOT/trimming 链路（对齐决策文件 M5/M2 建议）。
2. **source-gen 桥（把"生成式"留在库层）。** "编译期生成代码"的需求在 VBScript.NET 侧由**源生成器 + 编译器即库（Roslyn API）**承接，与 PROPOSAL C 一致：任何"生成式"都不进语言表面，只进库 API。若要服务 VBScript 迁移（样板代码生成），提供一个 .vbx 化的 source-gen 接口（声明式、独立进程、可缓存），而不是编译器内脚本。
3. **识别新元数据（必须桥接的硬义务）。** VBScript.NET 编译器须识别 `[Experimental]`、interceptor 相关特性、`CompilerFeatureRequired`、以及 unsafe-evolution 的 `RequiresUnsafeAttribute`/`MemorySafetyRulesAttribute`，否则无法正确校验/消费 C# 产出的程序集（对齐决策文件 M8）。
4. **跟踪两条气压计，而不是自造方向。** 索引 T1 明确指出 C# 是 CLR 新特性与 .NET 生态的主要推动者；VBScript.NET 在"编译期可编程性"上应**跟随 C# 已证实的受控机制**，不带头引入 A 类方案。

### 对既有 RESOLUTION / 三态判定的影响

- **整体是确认，不是动摇。** C# 现实方向（受控 source-gen 替代运行时动态、类型系统承担更多职责、interceptors 定位为 source-gen 实现细节）为 RESOLUTION 的 **① Reject PROPOSAL A、② PROPOSAL C 为当前立场**提供了生态级旁证——本提案的"受控机制路线"不是 VB 一厢情愿，而是与 C# 主线同构的选择。
- **激活信号 (c)（"C#/Roslyn 若采用通用编译器脚本机制则重审"）当前未触发。** 截至索引所覆盖的 2026 年，C#/Roslyn **没有**通用编译器脚本/插件机制；最接近的仍是受控 source-gen + experimental interceptors。故 (c) 保持未触发。
- **但 (c) 的"先例"需要精确化**：2018-02-07 的 "We'll postpone this until we understand the uptake in C#" 针对的是**某个具体机制**的采用率。source generators 的 uptake 已经发生并成为生态常态（C# 9/10 落地、Regex/`INotifyPropertyChanged`/AOT 场景大量采用）——这验证的是 **PROPOSAL C 对应的受控路线**可行，而非 PROPOSAL A。若将来 C# 真的把"通用编译器脚本"做成受控特性，那也应并入 (c) 触发重审、并按"跟随已证实机制"处理，而不是复活 A。
- **三态判定保持 Table（No Plans）。** C# 侧证据使 Table 更稳：A 与 C# 生态方向冲突、C 与 C# 生态方向同构、B 多余、D（保持 inactive）在生态证据下是理性默认。

### 引用纪律与已核实原文

本附录引用的 C# 原文全部经 `..\..\..\csharplang` 逐字核实，来源如下：

- 「Incremental source generators: improve the source generation experience in large projects by breaking down the source generation pipeline and caching intermediate results.」→ `Language-Version-History.md`（C# 10 条目）
- 「The goal being to expand the set of scenarios in which these methods can work with source generators as well as being a more general declaration form for C# methods.」→ `proposals\csharp-9.0\extending-partial-methods.md`（Summary）
- 「This feature is intended to help make code AOT-friendly by allowing source generation to replace runtime reflection.」→ `meetings\2023\LDM-2023-02-27.md`
- 「As a C#-only feature, it could negatively affect the F# and VB ecosystems.」→ `meetings\2023\LDM-2023-02-27.md`
- 「This approach is necessitated for all the reflection-based scenarios that use information that exists outside the type system to affect runtime code; because these scenarios use information not statically available during compilation, it is hard to make them AOT-compatible.」→ `meetings\2023\LDM-2023-07-24.md`
- 「we think that we need to take another look at the scenarios that are considering interceptors and see if we can put that information back into the type system」→ `meetings\2023\LDM-2023-07-24.md`
- 「We want them to be an implementation detail of certain source generators.」→ `meetings\working-groups\interceptors\IC-2023-04-04.md`
- 「We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either.」→ `proposals\unsafe-evolution.md`（「VB」小节）

**OPEN QUESTIONS**（无法核实的点，均不臆造）：

- Source Generators / Incremental Generators 的**规范正文**不在 csharplang（在 dotnet/roslyn 与官方博客）；csharplang 内无其独立 prose spec。`Suspect`：语言设计决策记录散布于 `meetings\2020\LDM-2020-02-19.md`、`LDM-2020-04-15.md` 等，但未逐一深挖。
- NativeAOT / trimming 的精确语言层约束主要在 dotnet/runtime，csharplang 内只有 LDM 讨论（如 `LDM-2021-05-12.md`、`LDM-2023-07-24.md`），无单一权威正文。
- interceptors 的最终状态：experimental，可能 pull（索引 OPEN QUESTIONS 同此判断）。若其状态变化，本附录第三节的"元数据桥接义务"需要随之重审。
