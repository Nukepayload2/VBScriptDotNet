# ModVB 语言设计 — 索引（VBScript.NET 评估用）

## 头部

- **本文件用途**：为 VBScript.NET 各提案的 meeting agent 提供 `modvb` 目录（**Anthony 的 ModVB 提案库——ModVB 与 VBScript.NET 是两个产品，本目录只是 VBScript.NET 的重要参考来源，不可等同**）的浓缩背景与文件地图，节省逐个读库的 token。
- **如何使用**：
  1. 先读「一、ModVB 现实方向摘要」快速建立世界观；
  2. 若某主题与手头提案相关，到「二、关键文件索引」用 Windows 反斜杠路径 + Grep 关键词深挖原文（提案）与决议（会议纪要）；
  3. 手头提案的「VBScript.NET 应对」应参考 **`decisions.md`（VBScript.NET 设计决策记录）** 的 M1–M8 与 D1–D4；C# 事实见 `..\csharplang-index.md`，VB 主线事实见 `..\vblang-index.md`。
- **来源目录**：`modvb\`（Anthony ModVB 提案库，VBScript.NET 参考评估用）。结构（**按 csharplang 四态组织**）：
  - **`proposals\` 根 = active**（37 份，RESOLUTION Active/Consider，设计在推进）；
  - **`proposals\inactive\` = inactive**（60 份，RESOLUTION Table，搁置/未定型）；
  - **`proposals\rejected\` = rejected**（5 份，RESOLUTION Reject，否决）；
  - **`proposals\vbscript-<版本>\` = done**（暂无成员，归档机制见 `proposals\vbscript-1.0\README.md`）。
  - **会议纪要 1:1 镜像**：`meetings\`（37）+ `meetings\inactive\`（60）+ `meetings\rejected\`（5）= 102 篇，**与提案 1:1 对应**：`meeting-<slug>` ↔ `proposal-<slug>`，2026-08 生成，每篇含 RESOLUTION/三态判定/五维评价/「附录：C# 生态与互操作考量」。
- **提案源头**：`AnthonyDesign_wordpress.txt`（Anthony D. Green 博客《The Agenda – A Visual Guide》全文，2026-04-27，HTML 格式，18 章 + Wrap-up）。`proposals\README.md` 已按四类状态给 102 提案索引（编号 01–102），本索引以其为骨架。
- **活跃度提示**：modvb 是 VB 主线之外 Anthony 的**「沙盒式激进延伸」**——主线「默认跟随 C#、扩展设高门槛」，Anthony「故意不跟 C#/F#、大规模扩张」（对照见 `evaluation-standard.md` 2.3 表）。102 份提案中**多数会议裁定为 Table（搁置）**，少量 Active/Consider，零星 Reject；判定依据 `evaluation-standard.md` 五维框架（效果/特性/品质/属性/炼金成分）。
- **关键文件**：`decisions.md`（M1–M8 决策映射 + D1–D4 决策修正，**VBScript.NET 侧唯一权威**）、`modvb\evaluation-standard.md`（五维评价标准 + LDM 追问清单）、`modvb\meetings\PROGRESS.md`（会议生成台账，G0–G25/C0–C25 分组）。

---

## 一、ModVB 现实方向摘要（分主题）

> 主题划分按 `proposals\README.md` 的 18 章节聚类。三态判定 = 各会议纪要「三态判定」小节结论。

### T1 治理与结构（沙盒式激进延伸）
- modvb 提案源自 Anthony《The Agenda》18 章；主线 VB LDM 停摆后，一批 VB 语言工作转入本目录推进（见 `..\vblang-index.md` 头部活跃度提示）。
- **目录即状态**（仿 csharplang）：根目录 = active（Active/Consider）、`inactive\` = Table（搁置/未定型，含原章 18「Needs more bake time」28 份实验想法 + 从根下沉的 Table 提案）、`rejected\` = Reject（否决）、`vbscript-<版本>\` = done 归档（暂无成员）。多数章 18 想法仅一句观察、无语法无设计，会议统一判 Table（保持 inactive），个别（scripting-interpreted 98、method-level-imports 38 等）Consider。
- 会议纪要为**生成件**（`PROGRESS.md` 台账），每篇含 RESOLUTION（`Active / Consider / Table / Reject` 四态或拆分判定）+ 五维评分 + C# interop 附录；**提案与会议 1:1 同名**，用提案路径直接推断会议路径。

### T2 与 C#/CLR 对齐与决策映射（decisions.md M1–M8 全景）
- `decisions.md` M1–M8 已给出八条「C# 现实方向 → VBScript.NET 应对」映射，本索引提案表逐行标注命中编号：
  - **M1** native-instruction-helpers（安全 IL 出口）；
  - **M2** any-pseudotype / postfix-casting / typeless-declarations（动态/晚期绑定差异化答案）；
  - **M3** return-byref / out-arguments（byref 互操作）；
  - **M4** type-predicates / intersection-union-types / shapeof-pattern-matching（类型系统与模式匹配）；
  - **M5** runtime-library / scripting-interpreted / generative-compiler-scripting（脚本运行时 vs source-gen/AOT，最大摩擦点）；
  - **M6** units-of-measure / string-span-utf8（数值/字符串底层类型）；
  - **M7** delegate-enhancements / interface-delegation / implicit-interface-implementation（ref struct 接口 / DIM / extensions）；
  - **M8** 冲突/脱节提示（unsafe 元数据桥接、dynamic vs AOT）——被最多提案引用。
- 含义：**C# 演进快、VB 主线慢；modvb 既要差异化又要桥接**——绝大多数会议附录的 C# 对照以「识别 C# 新元数据属性（NullableAttribute/OverloadResolutionPriority/RefSafetyRules 等）」为桥接义务。

### T3 类型推断与流分析（章 1：提案 01–04）
- 主线：**TypeOf 流分析**（01）与**可空性流分析**（02）是「同一流分析引擎的两张脸」，零新语法、编译期类型收窄；01 Active（启用型规则对冲），02 拆双轨（可空值类型 Active / 可空引用类型 Table）。
- 推断增强：If() 最佳公共类型（03 Consider，拆分采纳目标类型化）、递归/互递归 Lambda 推断（04 Consider，显式签名自引用 Active、互递归 Table）。
- 对 VBScript.NET：编译期类型系统流分析是脚本静态化的地基，与 null-safe、ShapeOf 模式匹配共享引擎。

### T4 样板消除与声明现代化（章 2 + 8 部分：提案 05–12, 36–41）
- **顶级代码/沉浸式文件**（05）是 VBScript.NET 脚本化身份的核心（窄子集 Consider→候选 Active；`.vbxhtml`/notebook 全家桶 Table）；`Key` 字段自动构造（06 Consider，B1 类字段方向采纳）。
- 通配符 Lambda `*.Member`（07 Consider）、简写属性/事件（08 Active，仅 `Return` 表达式体落地）、Markdown 文档注释（09 Table）、`#Ignore Warning`（10 Table）、隐式行继续（11 Consider）、杂项修复（12 拆分 1/2/4/7 Active）。
- 声明现代化：Out 实参（36 **Active**，Try*/模式方法地基）、模块增强（37 Table，泛型非提升窄子集 Consider）、方法级 Imports（38 Consider）、Bit Enum（39 Table，声明侧 Consider）、委托增强（40 Consider，`+=`/`-=` 合并 Active）。
- 对 VBScript.NET：样板消除是「低仪式感」定位的直接实现，多为纯增量零破坏。

### T5 语句与表达式现代化（章 3：提案 13–24）
- 声明/赋值：Let 局部声明（13 Consider，As New 数组 Active/Let 本身 Table）、Set 赋值语句（14 Table，解构赋值 Consider）。
- 控制流增强：Select Case（15 Table，类型分派并入模式家族）、For（16 Consider，多计数器/Exit-Continue Active）、For Each（17 Consider，解构/命名跳转 Active）、Do 循环头（18 Consider）、With（19 Consider，`.Me` Active/`&=` Reject）、Throw 推断（20 Table，`Is Nothing→ArgumentNullException` 窄种子 Table）。
- 异常/资源：Try 增强（21 **Active**，Catch/Finally 内 Await）、Using/SyncLock（22 Table，解构头 Active）、后置转换 `(As Type)`（23 Table，M2 类型化出口）、Return 写回 ByRef/Out（24 **Active**，M3）。
- 对 VBScript.NET：语句级语法是脚本「读起来像命令」的核心面；多按「拆分捆绑、分级推进」处置。

### T6 UI / XML / JSON / 字符串与模式匹配（章 4–7：提案 25–35）
- UI/XML：XAML 字面量（25 Table，source-gen 契约 Consider）、嵌入 VB 模式 `<?vb?>`（26 Table，模板走 source-gen）、XML Schema 类型（27 Table，注释型变体 Consider）。
- 数据形态：JSON 字面量（28 **Consider**，JsonObject 数据字面量，VB 领先 C# 的差异化点）、JSON 模式匹配（29 Table，拆并模式家族）、字符串模式匹配（30 Table，插值逆运算破坏风险最重）、字符串窄化转换（32 Table，枚举窄化 Consider）。
- 通用模式匹配（最核心差异化语法面）：ShapeOf（33 Table，无关键字 `Case pn As Type` 归家族 Phase 1）、用户定义模式方法（34 Table，Phase 2）、命名模式（35 Table）。
- 对 VBScript.NET：**模式匹配家族**（ShapeOf/Matches + 用户定义模式方法 + JSON/字符串模式）是「VBScript 运行时分发血统（TypeName/IsObject/late binding）」的现代化，需统一家族文法后分批复活。

### T7 LINQ 增强（章 9：提案 42–47）
- 范围表达式 `1 To 10 Step 2`（42 Table，仅迭代源子集 Consider）、查询增强（43 Consider，目标类型化 Select + BC36606 修复值得做）、查询理解 Include（44 Table，语言关键字不值得）、Insert/Update/Delete 表达式（45 Table，表达式副作用与 VB 根基冲突）、初始化器增强（46 Consider，With+From Active）、In/NotIn 运算符（47 Table，区间碎片挂起）。
- 对 VBScript.NET：查询语言守「只做纯语法重写、不识别 provider 语义」纪律，与 C#「BCL 先行、语法殿后」同构。

### T8 动态编程与晚期绑定（章 10：提案 48–50）
- **Any 伪类型**（48 Consider，M2 核心：逐变量显式晚期绑定替代 Object；须正名血缘——VB6 动态类型是 `Variant` 而非 `Any`）、无类型声明（49 Table，收进脚本模式 D）、Default 方法（50 Consider，可调用对象 v1 采纳）。
- 对 VBScript.NET：**这是 VB 最大特色面、也是与 AOT 张力最大面**——「默认安全、按需动态」路线（decisions M2/D3）的承载者，`(As Type)` 后置转换为类型化出口。

### T9 异步编程（章 11：提案 51–55）
- Async Sub 返回类型（51 Reject，签名是源码投影非配置投影）、Agile Async/省略 Await（52 Table，省略 Reject）、强制 Await（53 Reject 语言强制/Active 官方分析器）、Await Each 与 Async Iterator（54 Consider，消费端先落地）、Async Event（55 Reject，库方案覆盖）。
- 对 VBScript.NET：异步「跟随平台而非发明语法」，`Async Sub` 默认 void/fire-and-forget 语义不可重载，await 遗漏用官方分析器治理（与 C# CS4014 同构）。

### T10 Null 与 Nothing（章 12：提案 56–60）
- Null 字面量（56 Table，与 VB 基因冲突、依赖未定型 NRT）、`?=`/`?<>` 二值逻辑相等（57 Table，不引新语法走 EqualityComparer）、`??`/空指示符（58 Table，**VB 已有 `If(a,b)` 二参空合并**，`??` 是第二拼写 Reject）、`Do Nothing`（59 Table，安全但不值表面）、空安全行为全家桶（60 Reject 捆绑，析出空条件赋值/Await 两窄子集 Consider）。
- 对 VBScript.NET：Null 安全地基是**可空性流分析（02 Active）+ 未来 NRT 元数据识别（90 Table）**；`?` 符号预算已耗尽，新语法须避开与 `!`/类型字符的冲突。

### T11 声明式编程与代码生成（章 13：提案 61–64）
- 智能属性（61 Table，收敛为内置处理器集）、Replaceable/Replaces 替换修饰符（62 Table，MustReplace 种子 Consider）、Partial 成员（63 Consider，非 Private+恰一实现 Active）、语义预处理 `##If`（64 Consider，最小可行形态=扩展 `#If`）。
- 对 VBScript.NET：编译期可编程性走**受控机制**（源生成器/分析器/`##If`/Replaces），明确 Reject「编译器内跑任意脚本」，与 C# source-gen 生态方向同构（M5）。

### T12 类型系统与继承/接口/扩展（章 14–15：提案 65–71）
- 类型系统：交集/并集类型（65 Consider，交集限局部变量先行）、数组伪类型 `Array(Of T)`（66 Table，按大小实例化 Consider）、日期/时间字面量（67 **Active**，仅毫秒）。
- 继承/接口/扩展：覆写签名放宽（68 Consider，协变返回）、隐式接口实现（69 部分采纳，协变返回 Active/裸隐式 Reject）、接口实现委托给字段（70 Consider）、扩展属性（71 Consider，v1 只读）。
- 对 VBScript.NET：类型系统增强是「类型系统承担更多职责」（AOT 压力下与 C# unions 同向，M4）的 VB 表达，须识别 C# 新元数据（M8）。

### T13 性能、互操作与运行时库（章 16–17：提案 72–74）
- 名称解析优化（72 Consider，延续驱动回退修复 BC30456）、InitOnly/MustInit（73 Consider，对齐 C# init/required）、运行时库改进（74 Table，伞形拆分：性能 Active、异常 Consider、晚期绑定 Table）。
- 对 VBScript.NET：转换/晚期绑定/My 是脚本体验承重墙；运行时库工作是「语言即运行时」的直接支撑（M5）。

### T14 inactive 实验面全景（章 18 + 下沉 Table：提案 75–102 + 01–74 的 Table 项）
- **inactive 池 = 60 份**：原章 18 实验想法 28 份（75–102）+ 从根目录下沉的 Table 提案 32 份（01–74 中判 Table 者），全部保持 inactive（Table）。多数无语法仅一句观察；代表性方向：目标类型化转换（75）、Guarded Let（76）、独占上界 `To <`（78）、并行扩展（79）、Retry/Resume 弃用论（80）、JSON 序列化器（82）、大小写不敏感（83）、字符串模式前瞻/回溯（84）、String/Span/UTF-8（85，M6）、模式作为数据（87）、PLINQ/异步查询（88）、ConfigureAwait 选项（89）、**可空引用类型 NRT**（90 Table，切片 1 流分析 Active）、重复声明/Appendable（91）、生成式编译器脚本（92，作者自评坏主意）、**度量单位**（93，M6）、注释类型（94）、Case 类/判别联合（95）、结构约束重载（96）、**平台原生指令辅助**（97，M1）、**脚本与解释执行**（98 **Consider**，产品核心身份，已升入根目录 active）、链式三元（99）、类型谓词（100，M4）、版本化工具（101）、Rust 所有权（102，Reject 语言特性/保留诊断）。
- 对 VBScript.NET：inactive 是**路线图后备池**——多数需「激活信号」（数据、C# 先例、家族文法落地）才能复活，见各会议三态判定的复活信号。

---

## 二、关键文件索引（深挖用）

路径均为 Windows 反斜杠（相对 `modvb\`）；关键词供 Grep 定位。**目录即状态**：`proposals\` 根=active、`proposals\inactive\`=Table、`proposals\rejected\`=Reject；会议纪要同名且在 `meetings\` 对应子目录。三态判定取自对应会议纪要「三态判定」小节。

### 提案（proposals）— 章 1 类型推断（01–04）
| 路径 | 一句话要点 | 关键词 | 状态 | 三态判定 | Mx |
|---|---|---|---|---|---|
| `proposals\proposal-typeof-flow-analysis.md` | TypeOf Is/IsNot 流敏感类型收窄，免强转直接访问收窄类型成员 | TypeOf Is, 类型收窄, Select Case TypeOf | active | Active（限定范围） | M4 |
| `proposals\proposal-nullability-flow-analysis.md` | 可空类型空状态流分析，IsNot Nothing 守卫后视为非空 | Nullable(Of T), 空状态流分析, NRT | active | Active(轨道1)/Table(轨道2) | M8 |
| `proposals\proposal-conditional-best-common-type.md` | If() 推断最近公共基类型替代 Object，编译期解析公共成员 | If(), best common type, LUB | active | Consider | M8 |
| `proposals\proposal-recursive-lambda-inference.md` | 递归/互递归 Lambda 类型推断正确工作 | 递归Lambda, SCC, 本地函数 | active | Consider | M2/M4/M7 |

### 提案（proposals）— 章 2 精简与样板消除（05–12）
| 路径 | 一句话要点 | 关键词 | 状态 | 三态判定 | Mx |
|---|---|---|---|---|---|
| `proposals\proposal-top-level-code.md` | 沉浸式文件：Module/Class 之外直接写语句，整文件即程序 | 顶级代码, 沉浸式文件, .vbxhtml | active | Consider（窄子集候选 Active） | M5/M8 |
| `proposals\proposal-key-fields-auto-constructors.md` | Key 修饰符：类字段自动构造参数，值对象自动 Equals/GetHashCode | Key, 自动构造函数, IEquatable | active | Consider | M2/M5/M7/M8 |
| `proposals\proposal-wildcard-lambdas.md` | `*.Member` 通配符取代单参纯成员访问 lambda | 通配符 Lambda, 表达式树等价, EF 配置 | active | Consider | M2/M8 |
| `proposals\proposal-abbreviated-properties-events.md` | 压缩属性/事件声明样板：Return 表达式体、End 省略等五合一 | 简写属性, Return 表达式体, Set(value) | active | Active（仅 Return 表达式体） | M8 |
| `proposals\inactive\proposal-markdown-doc-comments.md` | 文档注释改用 Markdown 小节/@引用/围栏示例 | 文档注释, Markdown, XML doc | inactive | Table/Consider | M8 |
| `proposals\inactive\proposal-ignore-warning-directive.md` | 单行 `#Ignore Warning` 按错误号关段内警告 | #Ignore Warning, 错误号抑制 | inactive | Table | M8 |
| `proposals\proposal-implicit-line-continuations.md` | Then/Handles/Implements 前及 `) As` 间新增隐式行继续 | 隐式行继续, Then, Handles | active | Consider | M8 |
| `proposals\proposal-minor-fixes.md` | 七项小修复打包：Async Main、Optional 推断等逐项裁决 | Async Main, Optional 默认值, NameOf | active | 拆分(1/2/4/7 Active) | M8 |

### 提案（proposals）— 章 3 语句与表达式现代化（13–24）
| 路径 | 一句话要点 | 关键词 | 状态 | 三态判定 | Mx |
|---|---|---|---|---|---|
| `proposals\proposal-local-declarations.md` | Let 声明关键字与元组解构，修复 As New 数组/匿名类型 | Let, 元组解构, As New | active | Consider（As New 数组 Active） | M2 |
| `proposals\inactive\proposal-set-statement.md` | 显式 Set 赋值语句，支持复合赋值/多重赋值/解构赋值 | Set, 复合赋值, 解构赋值 | inactive | Table | M4/M8 |
| `proposals\inactive\proposal-select-case-enhancements.md` | Select Case 支持类型/形状/恒等/Like/集合成员匹配 | Select Case, TypeOf, ShapeOf | inactive | Table | M4/M8 |
| `proposals\proposal-for-enhancements.md` | For 多计数器、Exit/Continue 指定层、按迭代捕获修复 | For, Exit For, 多计数器, BC42324 | active | Consider | M8 |
| `proposals\proposal-for-each-enhancements.md` | For Each 元组解构、命名跳转、Where 过滤、Await Each | 元组解构, Where, Await Each | active | Consider | M5/M8 |
| `proposals\proposal-do-enhancements.md` | Do 循环头先赋值再测条件，消除重复读取样板 | DoTopLoopStatement, While/Until | active | Consider | M2/M5/M8 |
| `proposals\proposal-with-enhancements.md` | With 命名变量、.Me 伪成员、复合赋值 | With, .Me, 命名 With 变量 | active | Consider（.Me Active） | M8 |
| `proposals\inactive\proposal-throw-inference.md` | 裸 Throw 依条件形态推断异常类型，简化参数校验 | Throw, ArgumentNullException | inactive | Table | M8 |
| `proposals\proposal-try-enhancements.md` | Try 块内 Await、头块级声明、Catch/Finally 挂任意块 | Await, Catch/Finally, Try头声明 | active | **Active**（Await in Catch/Finally） | M5/M8 |
| `proposals\inactive\proposal-using-synclock-enhancements.md` | Using/SyncLock 块内 Catch/Finally、头解构、按名 Dispose | Using, SyncLock, 解构头, Dispose | inactive | Table（解构头 Active） | M5/M8 |
| `proposals\inactive\proposal-postfix-casting.md` | 后置转换 `expr(As Type)`，链式转换更可读 | 后置转换, As Type, CType | inactive | Table（D3 已定型语义） | M2 |
| `proposals\proposal-return-byref.md` | Return 返回函数值同时写回 ByRef/Out 输出参数 | Return, ByRef, Out, 命名实参 | active | **Active** | M3 |

### 提案（proposals）— 章 4–7 UI/XML/JSON/字符串/模式匹配（25–35）
| 路径 | 一句话要点 | 关键词 | 状态 | 三态判定 | Mx |
|---|---|---|---|---|---|
| `proposals\inactive\proposal-xaml-literals.md` | `<?xaml?>` 内联 XAML 声明，编译器降级为 UI 对象树 | XAML 字面量, {Binding}, 声明式 UI | inactive | Table（A 附复活信号） | M5/M8 |
| `proposals\inactive\proposal-embedded-vb-mode.md` | `<?vb?>` 开启 VB 解析模式，HTML 即 XML 字面量 | 嵌入解析模式, XML 字面量, 隐式 Return | inactive | Table（C source-gen Consider） | M5/M8 |
| `proposals\inactive\proposal-xml-schema-types.md` | 语言级 `<geo:Address>` 类型标注恢复 XML IntelliSense | XML 轴属性, XSD, 注释型变体 | inactive | Table（注释变体 Consider） | M2/M5/M8 |
| `proposals\proposal-json-literals.md` | `{}` JSON 字面量做目标类型化创建与 `&=` 写给 writer | JSON 字面量, JsonObject, System.Text.Json | active | Consider（数据字面量） | M4/M5/M8 |
| `proposals\inactive\proposal-json-pattern-matching.md` | 用 JSON 形状做模式匹配断言与分发，配 JSON Schema 标注 | ShapeOf, JSON 模式匹配, JSON Schema | inactive | Table（形状模式 Consider） | M4/M5/M8 |
| `proposals\inactive\proposal-string-pattern-matching.md` | 插值字符串当模式，`{message}` 占位符抽取片段绑定变量 | 插值逆运算, 字符串模式, Like | inactive | Table（B 子集 Consider） | M4/M6/M8 |
| `proposals\inactive\proposal-interpolated-string-optimization.md` | 插值字符串优化：合并相邻、常量孔折叠、StringBuilder `&=` | 插值字符串, 常量孔折叠, StringBuilder | inactive | Table（B→Active） | M6/M8 |
| `proposals\inactive\proposal-string-narrowing-conversions.md` | 字符串窄化转换：Parse 常量、TryParse 配 ShapeOf、转枚举 | 字符串窄化, Parse, TryParse, 枚举 | inactive | Table（枚举窄化 Consider） | M3/M4/M8 |
| `proposals\inactive\proposal-shapeof-pattern-matching.md` | Select Case 按运行时类型分发并绑定强类型变量 | ShapeOf, Case pn As Type, 类型模式 | inactive | Table（归家族 Phase 1 Active） | M4 |
| `proposals\inactive\proposal-user-defined-pattern-methods.md` | 带 Out 参数返回 Boolean 的函数作模式，判定+取值一次完成 | 用户定义模式方法, Out, Pattern 标记 | inactive | Table（归家族 Phase 2 Active） | M4 |
| `proposals\inactive\proposal-named-patterns.md` | 函数即模式，以解构语法递归匹配对象形状 | 命名模式, 解构语法, 工厂镜像 | inactive | Table（概念 Active） | M3/M4 |

### 提案（proposals）— 章 8–11 声明/LINQ/动态/异步（36–55）
| 路径 | 一句话要点 | 关键词 | 状态 | 三态判定 | Mx |
|---|---|---|---|---|---|
| `proposals\proposal-out-arguments.md` | Out 实参（调用点隐式声明）与只写 Out 形参 | Out, TryGetValue, OutAttribute | active | **Active** | M3 |
| `proposals\inactive\proposal-module-enhancements.md` | 模块可泛型/嵌套，默认不提升成员，StandardModule 回退 | 泛型模块, StandardModule, hoist | inactive | Table（泛型非提升 Consider） | M8 |
| `proposals\proposal-method-level-imports.md` | 方法级 Imports 收窄命名空间/Shared 成员导入作用域 | 方法级Imports, 作用域收窄, XML命名空间 | active | Consider | M8 |
| `proposals\inactive\proposal-bit-enum.md` | 位枚举专用语法：成员自动 2 的幂、逗号掩码、位段 | Bit Enum, 掩码, 位段, flags | inactive | Table（声明侧 Consider） | M2/M8 |
| `proposals\proposal-delegate-enhancements.md` | 委托 `+=`/`-=` 合并 + `<Function(...)>` 匿名委托类型 | 委托合并, Delegate.Combine, AddressOf | active | Consider（`+=` Active） | M7/M3/M5/M4 |
| `proposals\rejected\proposal-pipeline-operator.md` | 管道运算符 `->` 改写嵌套调用为从左到右数据流 | 管道运算符, 嵌套调用, It 记号 | rejected | Reject（窄方案 Consider） | M8 |
| `proposals\inactive\proposal-range-expressions.md` | `1 To 10 Step 2` 范围表达式字面量作 For Each 迭代源 | 范围表达式, To/Step, 等差序列 | inactive | Table（仅迭代源 Consider） | M8 |
| `proposals\proposal-query-enhancements.md` | 查询 From 元组解构、Select 聚合、目标类型化、BC36606 修复 | 元组解构, 聚合函数, 目标类型化 | active | Consider | M2/M5/M8 |
| `proposals\inactive\proposal-query-comprehensions.md` | For Each 内 Include 关联加载理解 + 六个未展示项 | Include, Skip Until, Left Join | inactive | Table | M5/M8 |
| `proposals\inactive\proposal-insert-update-delete-expressions.md` | 声明式对数据源增删改的 Insert/Update/Delete 表达式 | Insert, Update, Delete, DML, DTO | inactive | Table | M2/M5/M8 |
| `proposals\proposal-initializer-enhancements.md` | 初始化器增强：嵌套、With+From 组合、`!` 字典访问 | With组合, From填充, 集合初始化器 | active | Consider（C Active） | M4/M5/M8 |
| `proposals\inactive\proposal-in-notin-operators.md` | In/NotIn 运算符映射 Contains 做成员测试与区间判定 | In, NotIn, Contains, 范围表达式 | inactive | Table | 无 |
| `proposals\proposal-any-pseudotype.md` | Any 伪类型做逐变量显式晚期绑定替代 Object | Any, 伪类型, 晚期绑定, Variant | active | Consider | M2 |
| `proposals\inactive\proposal-typeless-declarations.md` | 无类型声明默认改用 Any 动态（渐进类型化） | Any, 无类型声明, 渐进类型化 | inactive | Table（收进脚本模式） | M2 |
| `proposals\proposal-default-methods.md` | Default 方法使对象可像函数调用（sp()=sp.Invoke()） | Default Sub Invoke, 可调用对象 | active | Consider | M7 |
| `proposals\rejected\proposal-async-sub.md` | Async Sub 可声明 Task/ValueTask 返回类型并配置化默认 | Async Sub, As Task, ValueTask | rejected | Reject | M2/M5 |
| `proposals\inactive\proposal-agile-async.md` | Agile Async 修饰符免写 ConfigureAwait(False)，省略中间 Await | Agile Async, ConfigureAwait, Await 省略 | inactive | Table | M2/M8 |
| `proposals\rejected\proposal-require-await-call.md` | 异步调用必须 Await 或赋给任务对象，fire-and-forget 用 Call | Await, Call, fire-and-forget | rejected | Reject(语言)/Active(分析器) | M8 |
| `proposals\proposal-async-iterator.md` | IAsyncEnumerable 对称消费（Await Each）与构建（Async Iterator） | Await Each, Async Iterator, Yield | active | Consider（消费端先落地） | M2/M5/M8 |
| `proposals\rejected\proposal-async-event.md` | Async Event 声明异步事件，handler 可异步执行可被等待 | Async Event, RaiseEvent, AsyncEventHandler | rejected | Reject | M8 |

### 提案（proposals）— 章 12–17 Null/声明式/类型系统/继承/性能/运行时（56–74）
| 路径 | 一句话要点 | 关键词 | 状态 | 三态判定 | Mx |
|---|---|---|---|---|---|
| `proposals\inactive\proposal-null-literal.md` | Null 字面量统一空引用与空值，If() 推断强制提升可空 | Null, Nothing, 可空推断, NRT | inactive | Table | M2/M5 |
| `proposals\inactive\proposal-null-equality-operators.md` | `?=`/`?<>` 二值逻辑相等，双 null 相等，等价 IS DISTINCT FROM | 二值逻辑, ?=, 三值逻辑, T-SQL | inactive | Table | M8 |
| `proposals\inactive\proposal-null-coalescing.md` | 后置空合并 `??` 与空指示符 `?`（`True?`/`String?`/`lookup()?`） | 空合并, ??, 空指示符, NRT | inactive | Table（True? Consider） | M8 |
| `proposals\inactive\proposal-do-nothing.md` | 显式空操作语句 Do Nothing，为需语句但无事可做的位置占位 | Do Nothing, 空操作占位 | inactive | Table | 无 |
| `proposals\rejected\proposal-null-safe-behaviors.md` | `?.` 扩展到语句层，对象为 null 时循环/等待/赋值/事件整体跳过 | 空传播, 空条件赋值, AddHandler | rejected | Reject（两窄子集 Consider） | M8 |
| `proposals\inactive\proposal-smart-attributes.md` | 可组合特性注入属性/事件访问器行为，免源生成器 | PropertyHandlerAttribute, 特性注入 | inactive | Table | 无 |
| `proposals\inactive\proposal-replacement-modifiers.md` | Replaceable/Replaces/MustReplace 声明-替换协作契约 | Replaceable, Replaces, MustReplace | inactive | Table（MustReplace Consider） | M2/M5 |
| `proposals\proposal-partial-members.md` | Partial 扩展到任意成员，合并特性/Handles/Implements 子句 | Partial 成员, Handles, Implements | active | Consider | M5 |
| `proposals\proposal-semantic-preprocessing.md` | `##If` 语义预处理，TYPE_EXISTS/MEMBER_EXISTS 谓词条件编译 | ##If, TYPE_EXISTS, 语义预处理 | active | Consider | M5 |
| `proposals\proposal-intersection-union-types.md` | 即席交集 `{A,B}`/并集 `{A Or B}` 类型，多接口约束写声明处 | 交集类型, 并集类型, 合成类型符号 | active | Consider（交集限定局部变量） | M4 |
| `proposals\inactive\proposal-array-pseudotype.md` | 数组伪类型 `Array(Of T)` 拼写与 `New Array(Of T)(size)` 实例化 | Array(Of T), newarr, off-by-one | inactive | Table（按大小实例化 Consider） | M2/M8 |
| `proposals\proposal-date-time-literals.md` | `#...#` 日期字面量加毫秒/Kind，加 DateOnly/TimeOnly 等后缀 | 毫秒, DateTimeKind, DateOnly | active | **Active**（仅毫秒） | M8 |
| `proposals\proposal-override-signature-relaxation.md` | 覆写返回类型协变为派生类型（零新语法） | 协变返回类型, Overrides, widening | active | Consider（范围收缩挂起） | M7 |
| `proposals\proposal-implicit-interface-implementation.md` | 同名同签名公共成员自动满足接口实现，返回类型协变放宽 | 隐式接口实现, Implements, 协变返回 | active | 部分采纳（协变返回 Active） | M7 |
| `proposals\proposal-interface-delegation.md` | 接口实现委托给字段/属性，字段 Implements 接口即转发全部成员 | 接口委托, 组合优于继承, 转发方法 | active | Consider | M7 |
| `proposals\proposal-extension-properties.md` | 扩展方法机制推广到属性，`<Extension>` 只读泛型扩展属性 | 扩展属性, Extension 特性, 只读属性 | active | Consider（v1 只读） | M8/M5 |
| `proposals\proposal-name-resolution.md` | Imports 命名空间遮蔽致 BC30456 时延续驱动回退到唯一候选 | 名称解析, BC30456, 延续驱动 | active | Consider | M8 |
| `proposals\proposal-initonly-mustinit.md` | InitOnly/MustInit 属性修饰符：仅初始化期可写、必填编译期强制 | InitOnly, MustInit, 必填成员 | active | Consider | M8 |
| `proposals\inactive\proposal-runtime-library.md` | 运行时库八条改进：转换性能、异常质量、晚期绑定、My | 运行时库, 内建转换, 晚期绑定 | inactive | Table（性能/二进制 Active） | M5/M8 |

### 提案（proposals\inactive）— 章 18 实验性想法 + 下沉 Table（75–102 + 01–74 Table 项，60 份）
> 均保持 inactive（Table），多数无语法仅登记；仅标注例外（98 已升入根目录 active）。会议纪要同名于 `meetings\inactive\`。
| 路径 | 一句话要点 | 关键词 | 三态判定 | Mx |
|---|---|---|---|---|
| `proposals\inactive\proposal-target-typed-conversions.md` | 转换表达式省略类型实参，由目标类型推断 | DirectCast, TryCast, 目标类型化 | Table | M2 |
| `proposals\inactive\proposal-guarded-let.md` | Let 声明加 Then/Else/End Let 守卫分支 | Let 守卫, Then/Else, 流分析引擎 | Table | M4/M3/M5 |
| `proposals\inactive\proposal-case-else-variable.md` | Case Else 后跟变量名，绑定未匹配值 | Case Else, 变量绑定, 变量模式 | Table | M4 |
| `proposals\inactive\proposal-exclusive-for-upper-bound.md` | 范围表达式独占上界 `x To < y`（左闭右开） | To <, 独占上界, UBound | Table | M8 |
| `proposals\inactive\proposal-parallel-extensions.md` | 并行语言集成方向登记，现阶段 Parallel.For 已够用 | Parallel, Parallel.For, PLINQ | Table | M2 |
| `proposals\inactive\proposal-retry-resume.md` | Try 覆盖 Resume Next/GoTo 重试即可弃用 On Error | On Error, Resume Next, GoTo Retry | Table | M2/M8 |
| `proposals\inactive\proposal-robust-mapping.md` | null 键标注有意不映射成员，ShapeOf 校验反序列化完整性 | null键, ShapeOf, 自然连接映射 | Table | M2/M4/M5/M8 |
| `proposals\inactive\proposal-json-serializers.md` | VB 写 JSON 序列化器应有自己的「自然抽象」 | Utf8JsonReader, JsonNode, 源生成器 | Table | M2/M5/M6/M8 |
| `proposals\inactive\proposal-case-insensitivity.md` | 大小写不敏感/排序规则与内置字符串比较的集成 | Option Compare, StringComparer, 排序规则 | Table | M5/M8 |
| `proposals\inactive\proposal-string-pattern-lookahead.md` | 字符串模式前瞻/回溯能力，零分配，lazy-greedy | 字符串模式匹配, 回溯, 零分配 | Table | M5/M7/M8 |
| `proposals\inactive\proposal-string-span-utf8.md` | String 对 Span/UTF-8 保持优雅处理的注记 | Span, UTF-8, u8字面量, ref struct | Table | M6 |
| `proposals\inactive\proposal-named-pattern-inputs.md` | 命名模式实参可否承载输入（如 Regex 模式） | 命名模式, 值输入, 常量槽位 | Table | M4 |
| `proposals\inactive\proposal-patterns-as-data.md` | 模式成为可传递可组合的一等数据 | 模式作为数据, 具体化, Pattern(Of T) | Table | M8 |
| `proposals\inactive\proposal-plinq-async-queries.md` | 并行/异步/领域六查询方向登记册 | PLINQ, Await, IAsyncEnumerable | Table | M8 |
| `proposals\inactive\proposal-configureawait-options.md` | ConfigureAwait 选项与未观测异常配置 | ConfigureAwait, 未观测异常, 白鲸 | Table | M8 |
| `proposals\inactive\proposal-nullable-reference-types.md` | 为 VB 重设计 NRT，反转默认标记「保证非空」 | NRT, NotNothing, 空性契约 | Table（切片1 Active） | M8 |
| `proposals\inactive\proposal-duplicate-declarations.md` | 源生成器版多播委托处理重复定义/名称冲突 | Appendable, 多播委托, 重复声明 | Table | M8 |
| `proposals\inactive\proposal-generative-compiler-scripting.md` | 脚本参与/驱动/改写编译过程（作者自评坏主意） | 生成式编译器脚本, 可信边界 | Table | M5 |
| `proposals\inactive\proposal-units-of-measure.md` | F# 式度量单位，唯一无争议结论是泛型运算符 | 度量单位, Weight(in lbs), 量纲代数 | Table | M6 |
| `proposals\inactive\proposal-annotated-types.md` | XML/JSON 类型变体以注释标注，供 IDE 补全诊断 | 注释类型, 类型变体, 类型提供器 | Table | M2 |
| `proposals\inactive\proposal-case-classes.md` | Case 前缀修饰类/结构/接口构成封闭分支类型族 | Case类, 判别联合, 封闭类型族 | Table | M4/M8 |
| `proposals\inactive\proposal-structure-constraint-overloading.md` | 按 Class/Structure 约束分派泛型方法，隐藏假参数 | 约束重载, Structure约束, 假参数 | Table | M8 |
| `proposals\inactive\proposal-native-instruction-helpers.md` | 扩展内联列表+well-known 模块，更多 IL 经 VB 表达 | 内联列表, IL 指令映射, Localloc | Table | M1 |
| `proposals\proposal-scripting-interpreted.md` | 脚本化记录（「Of course.」）；真解释器 Reject，走脚本宿主+REPL | 脚本宿主, REPL, 顶层代码 | **active**（已升根目录） | **Consider** | M5 |
| `proposals\inactive\proposal-chained-ternary.md` | `? If(条件,值,…,Else,兜底)` 链式三元 | ? If, 链式三元, 多分支取值 | Table（`? If()` Reject） | M8 |
| `proposals\inactive\proposal-type-predicates.md` | 任意谓词即命名类型进流敏感类型，如 `{Integer, Positive}` | 类型谓词, 流敏感类型, 值类型精化 | Table（谓词型接口 Reject） | M4 |
| `proposals\inactive\proposal-versioning-tools.md` | 库版本化：跨命名空间移动/重命名/元命名空间/升级工具化 | TypeForwardedTo, 元命名空间, 二进制兼容 | Table | M2/M5/M6/M8 |
| `proposals\inactive\proposal-rust-ownership.md` | 任意块作用域、Rust 式所有权、AI 三张便签合并 | 任意块作用域, Rust所有权, 借用检查 | Table（所有权 Reject） | M8 |

### 会议（meetings）— 102 篇，与提案 1:1 对应
- **命名规则**：`meetings\meeting-<slug>.md` ↔ `proposals\proposal-<slug>.md`；inactive 在 `meetings\inactive\`、rejected 在 `meetings\rejected\`。提案表任一行的会议路径 = 提案路径 `proposals\` → `meetings\`、`proposal-` → `meeting-`。
- **每篇统一内容**：Agenda（提案概览 + vblang issue 关联）→ 场景/候选方案（PROPOSAL A/B/C…）→ 权衡 Q&A → VB 基因对照 → `RESOLUTION:` + `Implication:` → OPEN QUESTIONS/TODO → **「附录：特性评价」**（五维评分表 + 三态建议 + 返工建议）→ **「附录：C# 生态与互操作考量」**（相关 C# 现实方向逐字引用 + 现实 vs 提案 + 对 VBScript.NET 的适应建议 + 对 RESOLUTION 的影响 + OPEN QUESTIONS）。
- **代表会议（按主题深挖起点）**：
  - 流分析引擎：`meetings\meeting-typeof-flow-analysis.md`、`meetings\meeting-nullability-flow-analysis.md`
  - 模式匹配家族：`meetings\inactive\meeting-shapeof-pattern-matching.md`、`meetings\inactive\meeting-user-defined-pattern-methods.md`、`meetings\inactive\meeting-select-case-enhancements.md`
  - 动态/类型化出口：`meetings\meeting-any-pseudotype.md`、`meetings\inactive\meeting-postfix-casting.md`、`meetings\inactive\meeting-typeless-declarations.md`
  - 脚本化核心：`meetings\meeting-top-level-code.md`、`meetings\meeting-scripting-interpreted.md`
  - 声明式/生成器：`meetings\inactive\meeting-smart-attributes.md`、`meetings\inactive\meeting-replacement-modifiers.md`、`meetings\meeting-semantic-preprocessing.md`
  - Null 安全家族：`meetings\rejected\meeting-null-safe-behaviors.md`、`meetings\inactive\meeting-null-coalescing.md`、`meetings\inactive\meeting-nullable-reference-types.md`
  - 异步：`meetings\meeting-try-enhancements.md`（Await in Catch/Finally）、`meetings\meeting-async-iterator.md`、`meetings\rejected\meeting-require-await-call.md`
- **生成台账**：`meetings\PROGRESS.md`（G0–G25 会议生成分组 + C0–C25 C# 附录分组，已 102/102 完成）。

### 元文件（modvb 根 / proposals 根）
| 路径 | 一句话要点 |
|---|---|
| `decisions.md`（InternalDevDocs 根，已从 `modvb\` 移出——VBScript.NET 决策，非 modvb 决策） | **VBScript.NET 侧决策唯一权威**：D1–D4 决策修正（RefStructHelper 移植、NativeAOT 桥、postfix-casting 语义、P1 优先级判定）+ M1–M8 八条「C# 现实 → 应对」映射 |
| `modvb\evaluation-standard.md` | 五维评价框架（效果/特性/品质/属性/炼金成分）× 5 级评分 + LDM 追问清单 + 强提案品质清单/弱提案红旗 |
| `modvb\proposals\README.md` | **102 提案按四类状态分组的索引**（active 37 / inactive 60 / rejected 5 / done 0，编号 01–102 + 一行摘要），本索引骨架 |
| `modvb\proposals\rejected\` | 5 份 Reject 提案（41/51/53/55/60），否决留档 |
| `modvb\proposals\vbscript-1.0\README.md` | done 档归档机制说明（暂无成员，待 VBScript.NET 发布特性后按版本归档） |
| `modvb\proposals\WRITING-GUIDE.md` | proposal 撰写规范（vblang 六章节模板 + 证据阶梯） |
| `modvb\AnthonyDesign_wordpress.txt` | Anthony《The Agenda》原文全文（18 章 + Wrap-up，HTML），提案源头 |
| `modvb\upstream.url` | 博客原文链接（anthonydgreen.net，2026-04-27） |

---

## 三、对 VBScript.NET 的含义 → 已移至 `decisions.md`

> 原「三、对 ModVB / VBScript.NET 的含义（M1–M8 + D1–D4）」已独立成文，见 **`decisions.md`**（VBScript.NET 设计决策记录）。该文件同时记录决策修正（RefStructHelper 解法、NativeAOT 桥、postfix-casting 语义、P1 优先级判定规则）。本索引只保留 ModVB 事实与文件地图；决策与应对以 `decisions.md` 为准。

---

## 四、引用纪律提示

- 引用 ModVB/会议原文必须**逐字准确**并标注来源文件（用上述路径）；无法核实的标注 **Suspect** 或列入 **OPEN QUESTIONS**。
- 本索引中已核实可引用的原文与出处（供 meeting agent 直接使用）：
  - 「Look at this code I'm never going to have to type again!」→ `modvb\proposals\proposal-key-fields-auto-constructors.md`（Detailed design，Anthony 2.2 节）。
  - 「`# 41. Primary constructors.` Rejected for VB. Would have had parity with C# vNext feature.」→ `modvb\meetings\meeting-key-fields-auto-constructors.md`（候选方案 PROPOSAL C，主线 2014 判词）。
  - 「Rejected. The design team found this idea deeply unsettling.」→ `modvb\meetings\meeting-abbreviated-properties-events.md`（VB 基因对照，主线 #196 隐式后备字段判词）。
  - 「JSON is the lingua franca of the cloud. First-class JSON support could be a strong attractant for first-time developers.」→ `modvb\meetings\meeting-json-literals.md`（场景与缺口，vblang #101）。
  - 「The JSON thing is great. Oh, but it already works with `!` so all the value just evaporated.」→ `modvb\meetings\meeting-json-pattern-matching.md`（Q4，2017-08-23）。
  - 「We're proud not to do anything」→ 多处（源自主线 2014-02-17；如 `modvb\meetings\inactive\meeting-do-nothing.md` RESOLUTION 5、`modvb\meetings\meeting-wildcard-lambdas.md` RESOLUTION 7、`modvb\meetings\inactive\meeting-named-pattern-inputs.md`）。
  - 「Fantastic idea, and too hard to do.」→ `modvb\meetings\inactive\meeting-xaml-literals.md`（第 12 节）、`modvb\meetings\inactive\meeting-structure-constraint-overloading.md`（RESOLUTION 2）。
  - 「Control flow would be altered by a very subtle character.」→ 多处（主线 #167 `Return?` 判词；如 `modvb\meetings\rejected\meeting-null-safe-behaviors.md`、`modvb\meetings\inactive\meeting-chained-ternary.md`、`modvb\meetings\inactive\meeting-agile-async.md`）。
  - 「We will almost never make breaking changes to Visual Basic」→ `modvb\meetings\rejected\meeting-require-await-call.md`（权衡，引 2018-06-13 主线原则）。
  - 「Treat it as an optimization, not a feature.」→ 多处（`modvb\meetings\meeting-try-enhancements.md`、`modvb\meetings\inactive\meeting-interpolated-string-optimization.md`、`modvb\meetings\inactive\meeting-runtime-library.md`、`modvb\meetings\inactive\meeting-structure-constraint-overloading.md`）。
  - 「String handling in VB must be awesome!」→ `modvb\proposals\inactive\proposal-string-span-utf8.md`（Motivation）。
  - 「We have `TypeForwardedToAttribute` to move a type to a different assembly. What about moving it to a different namespace?」→ `modvb\meetings\inactive\meeting-versioning-tools.md`（场景与缺口）。
  - 「It doesn't earn back it's -100 points.」→ `modvb\meetings\inactive\meeting-rust-ownership.md`（附录：C# 生态，引 C# 2015 destructible types 判例）。
  - 「Void-returning async methods are my white whale.」→ `modvb\meetings\meeting-require-await-call.md`（场景与缺口）、`modvb\proposals\inactive\proposal-configureawait-options.md`（Motivation）。
- **OPEN QUESTIONS**（本索引未深挖、需自行核实的点）：各会议「三态判定」给出的是拆分判定——Active/Consider 项的具体**复活信号**（数据需求、C# 先例、家族文法落地顺序）需回读对应会议；模式匹配家族（ShapeOf/用户定义模式方法/JSON/字符串模式）各份会议互相要求对表后才返工，统一文法未定型；`decisions.md` M1–M8 与个别会议附录对 M 编号的引用偶有出入（如 interpolated-string-optimization 将 M7 关联到 ref struct 编译器支持，与 M7 定义中的 delegate-enhancements 语义不同），引用时以 `decisions.md` 为权威。
- **路径修正备注（2026-08-09）**：proposals/meetings 已按 csharplang 四态组织重排——根目录=active、`inactive\`=Table、`rejected\`=Reject、`vbscript-<版本>\`=done。旧版「状态列」将若干 01–74 提案误标 inactive 的问题已随重排消除；现在物理位置即状态。98 scripting-interpreted 升入根目录（Consider）。
