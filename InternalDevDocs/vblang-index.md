# Visual Basic Language Design — 索引（供 ModVB / VBScript.NET 评估用）

## 头部

- **本文件用途**：为 ModVB 各提案的 meeting agent 提供 dotnet/vblang 官方仓库的浓缩背景与文件地图，节省逐个读库的 token。
- **如何使用**：
  1. 先读「一、VB 语言设计现实方向摘要」快速建立世界观；
  2. 若某主题与手头提案相关，到「二、关键文件索引」用 Windows 反斜杠路径 + Grep 关键词深挖原文；
  3. 手头提案的「VBScript.NET 应对」应参考 **`decisions.md`（VBScript.NET 设计决策记录）**；VB 事实仍以本文件为准。
- **来源目录**：`vblang\`（dotnet/vblang 官方仓库镜像，含 2014–2018 LDM notes、proposals、Version 11 规范）。Governance 见该目录 `README.md`、`proposals\README.md`、`meetings\README.md`。
- **活跃度提示**：会议笔记（meetings/）止于 2018-12-19（模式匹配讨论）——根因是 VB 语言设计主推人 **Anthony D. Green 离职后无人推进 VB LDM**，一批 VB 语言工作转入 **ModVB**（本仓库 `modvb\`）。仓库本身未停更：2024-11/12 设计并更新 **Overload Resolution Priority** 提案（VB 17.13 落地），2019–2022 间多为文档维护（DocuTune 链接修复、typo）与 2021 年 CallerArgumentExpression 版本历史补充。VB 特性稀少（VB 15.5 后长期空白，至 VB 17.13 才新增两项）。**C# 是 CLR/生态的主要推动者，VB 多数新特性先在 C#/CLR 定平台决策后再对齐。**
- **版本对照**（Language-Version-History.md）：
  - **VB 15.0**（VS2017）：Tuples、Binary Literals、Digit Separators
  - **VB 15.3**（VS2017 15.3）：Inferred tuple element names
  - **VB 15.5**（VS2017 15.5）：Non-trailing named arguments、Private Protected、Digit separator after base specifier
  - **VB 17.13**（VS2022 17.13）：Recognizing 'unmanaged' constraint、Overload Resolution Priority
  - **VS2022 / .NET 6**：CallerArgumentExpression（**不受语言版本门控**，VS2022 17.0 起编译器可用）

---

## 一、VB 语言设计现实方向摘要（分主题）

### T1 治理与节奏（低活跃）
- VB 由 VB LDT 治理，流程：Discussion label 讨论 → 提案（proposal-template，四阶段 Proposed/Prototype/Implementation/Specification 勾选跟踪）→ Roslyn 原型 → 实现 → 修订规范 → 归档。提案四状态：active（在 proposals/ 根）、inactive / rejected（分目录）、done（按语言版本归档）。
- 会议笔记（meetings/）止于 2018-12-19。**停摆根因**：Anthony D. Green（VB 语言设计主推人）离职后无人继续推进 VB LDM，会议随之停摆，一批 VB 语言工作由此转入 **ModVB**（本仓库 `modvb\` 目录）推进。此后官方设计以提案/issue 为主（最近一次：2024-11/12 设计 Overload Resolution Priority，VB 17.13 落地）；VB 语言投入显著低于 C#，特性稀少而克制。
- 2018-06-13 成文决策原则（vbldm-notes-2018.06.13.md「Review process」）：**几乎不做 breaking change**、保持 VB-like 一致性、扩展表面区门槛高、C# 领跑 CLR/库层变更；并确立审阅标签体系（In Process / Considering / No Plans / Rejected）。

### T2 与 C#/CLR 的对齐与观望
- 多数新特性先在 C# LDM/CLR 定平台侧（元数据、底层类型），VB 再做语法与语义适配：字符串插值（2014-04-02、2015-01-14）、元组与 ByRef 返回（2016-05-06）、默认接口实现（2017-05-19）、nameof（2014-10）、Range（2018-02/03）、可空引用类型（2017-08-30、2018-02-07 均观望推迟）。
- 版本历史印证同源性：Tuples、Binary Literals、CallerArgumentExpression 等直接引用 csharplang/roslyn 提案。
- 含义：评估 VB 现状时，「VB 跟随 C#」是主线；**C# 演进快、VB 慢且常滞后**。

### T3 晚期绑定与动态互操作（VB 独有）
- `Object` 变量晚绑定（Option Strict Off 时成员解析基于运行时类型）、`!` 字典访问（`dict!key`）、`TypeOf...Is` / `Is` / `IsNot` 为 VB 特有语义。
- 2017-08-23 讨论降低对 Option Strict Off 的强依赖：Dynamic 伪类型、方法作用域 Option/Imports、`!` 后期绑定成员访问、擦除接口（多数为开放问题）。
- **运算符冲突**：可空引用类型的 null 容错运算符 `!` 与 VB 既有 `dict!key` 字典访问及 `Dim radius!`（Single 类型字符）冲突，是 VB 采用该 C# 特性时的障碍。

### T4 语言哲学与保守性（对 VBScript.NET 评估最关键）
- 拒绝**不「VB 化」**的特性：Null 字面量（「Nothing is more like default(T), and VB lacks null.」）、Declaration expressions、表达式体成员、主构造函数（均 2014-02-17 拒绝）；Records/模式匹配（04-23）未决交由 C# LDM。
- 保留双异常体系：结构化（Try/Catch/When/Finally）与**非结构化**（On Error/Resume/GoTo）并存，同一方法内不可混用——VB 区别于其他 .NET 语言的独特之处。
- 特有语法：`=` 兼赋值与相等、`&` 字符串拼接（一切转 String 视为 widening）、`If(x, y)` 三参条件（按需求值，非 IIF）、类型字符（% & @ ! # $）、行续行 `_`、注释 `'`/REM、`Optional`/`ByRef` 不参与签名。
- 原则原文：「We strongly believe that Visual Basic has a stance - a way of doing things.」

### T5 模式匹配与数据形态（2018 末主线）
- 2018-12-19 基于 issue #337 敲定：以 **`Matches`** 为关键字、分阶段发布（声明式模式 → 递归式 → and/or/not）、原则上跟随 C# 但保持 VB 风格，目标版本 **VB 16.2**；`Is` 因与引用相等用法歧义被否。
- 2017-10-18：JSON 字面量（#101，「JSON is the lingua franca of the cloud」）、标注类型/标签字符串字面量（#184）、Guid 字面量（#27）；JSON 模式匹配「搁置等反馈」。

### T6 提案现状（3 个活跃，库内无 rejected/inactive 实体）
- **overload-resolution-priority.md**（**2024-11/12 设计，VB 17.13 已落地**）：`System.Runtime.CompilerServices.OverloadResolutionPriority` 特性，让 API 作者调整同一类型内重载的相对优先级，引导调用方避开歧义/被 obsolete 的重载。
- **proposal-DerestrictedOperators.md**：解除运算符必须成对实现的限制（`<`⇔`>=`、`=`⇔`<>`、`IsTrue`⇔`IsFalse` 等），支持返回非 Boolean 中间类型的链式 range 检查；状态 Proposed/Prototype 完成、Implementation 进行中、Spec 未开始。
- **proposal-Implicit-default-optional-parameters.md**：`Optional` 省略显式默认值时隐式用类型默认值（Roslyn 中 >85% 的 Optional 声明即此情形）；Prototype 完成、Implementation 进行中。

### T7 规范（Version 11）
- `vblang\spec\` 是完整正文规范（14 章 + README 目录页），ECMA 风格，**ANTLR 双文法**（词法/句法，vb.g4 可下载），附 PDF/DOCX 外部链接。
- 兼容至上：「compatibility between versions must be preserved except when the benefit to language consumers is of a clear and overwhelming nature.」；逐级弃用流程贯穿引言章。
- 双模类型系统：「The Visual Basic programming language can be either a strongly typed or a loosely typed language.」——强/松散语义由 `Option Strict` 切换，是 VB 与 C# 最大的模型差异之一。

---

## 二、关键文件索引（深挖用）

路径均为 Windows 反斜杠；关键词供 Grep 定位。

### 提案（proposals）
| 路径 | 一句话要点 | 关键词 | 状态 |
|---|---|---|---|
| `vblang\proposals\overload-resolution-priority.md` | `OverloadResolutionPriority` 特性调整重载优先级；API 作者引导调用方避开歧义重载；**VB 17.13 落地** | OverloadResolutionPriority, overload resolution, ambiguity, narrowing | done（17.13） |
| `vblang\proposals\proposal-DerestrictedOperators.md` | 解除运算符成对实现限制（`<`⇔`>=` 等），支持返回非 Boolean 中间类型的链式 range 检查 | operators, complement pairs, IsTrue/IsFalse, range check | Proposed / Prototype / Impl 进行中 |
| `vblang\proposals\proposal-Implicit-default-optional-parameters.md` | `Optional` 省略默认值 = 类型默认值；>85% Optional 声明即此情形 | Optional, implicit default, Nothing, CallerInfo | Proposed / Prototype / Impl 进行中 |
| `vblang\proposals\proposal-template.md` | 提案模板：四阶段勾选 + Summary/Motivation/Detailed design/Drawbacks/Alternatives/Unresolved questions 六小节 | template, prototype, implementation | 元文件 |
| `vblang\proposals\README.md` | 提案四状态（active/inactive/rejected/done）与生命周期、目录约定 | proposal lifecycle, active, inactive, rejected, done | 元文件 |
| `vblang\proposals\inactive\README.md`、`vblang\proposals\rejected\README.md` | 空占位（两目录当前均无实体提案） | — | 空 |

### 会议（meetings）— 按年份
- **2014（12 场，Dev14/VS2015 设计期，最密集）**：
  - `vblang\meetings\2014\LDM-2014-02-10.md`：**Strict Module**（对齐 C# static class；可泛型但泛型模块不得含扩展方法，不可嵌套）
  - `vblang\meetings\2014\LDM-2014-02-17.md`：**Dev14 全部提案汇总**（~40 项，多数 approved/implemented；拒绝 Null 字面量、Declaration expressions、表达式体成员、主构造函数）
  - `vblang\meetings\2014\LDM-2014-03-12.md`：`ProtectedAndFriend`/`ProtectedOrFriend`（CLI FamilyAndAssembly/FamilyOrAssembly 精确转写）；CInt(Double) 编译期优化
  - `vblang\meetings\2014\LDM-2014-04-01.md`：`?.` 空传播结合性（left/right-associative）与代码生成；`Integer? → String` 空值处理
  - `vblang\meetings\2014\LDM-2014-04-02.md`：**字符串插值**（共识「meh」；VB 用 `$` 前缀 + `{}` 洞；语义恒等价 String.Format，非 Const）
  - `vblang\meetings\2014\LDM-2014-04-16.md` / `vblang\meetings\2014\LDM-2014-07-01.md`：**`#Disable Warning`/`#Enable Warning`**（注意两文件内容重叠：04-16 末尾已内嵌 07-01 的 Update 结论；ID 须按 VB 标识符规则、大小写不敏感）
  - `vblang\meetings\2014\LDM-2014-04-23.md`：**Records/不可变数据 + 模式匹配**（主构造函数、`matches` 运算符、自动 Equals/GetHashCode；未决，交 C# LDM）
  - `vblang\meetings\2014\LDM-2014-10-01.md`：**readonly 自动属性**在构造函数中赋值规则（走访问器、确定性赋值、不得超越 readonly field 的许可）
  - `vblang\meetings\2014\LDM-2014-10-08.md`：多继承接口歧义同名方法（hide-by-name vs hide-by-sig，最终选 PROPOSAL3：声明期合并、交重载解析）
  - `vblang\meetings\2014\LDM-2014-10-15.md` / `vblang\meetings\2014\LDM-2014-10-23.md`：**nameof** 规范 v4/v5（参数=普通表达式→允许 method-group/property-group；保留源码书写大小写；NameOf 为保留字）
- **2015（1 场）**：`vblang\meetings\2015\LDM-2015-01-14-VB.md`：字符串插值重载决议（**插值字符串「is a string」**，String 优先于 FormattableString；编译器发 factory 调用）
- **2016（1 场）**：`vblang\meetings\2016\LDM-2016-05-06-VB.md`：**ByRef Returns**（仅消费场景、不加新语法）+ **Tuples**（`(x As Integer, y As Integer)` 语法、逐元素转换、分解需加括号）
- **2017（8 场）**：
  - `vblang\meetings\2017\vbldm-notes-2017.04.12.md`：转换语法盘点与 4 提案（`As Type` cast、CInt 魔法方法、扩展 DirectCast、Checked/Unchecked + CVal）
  - `vblang\meetings\2017\vbldm-notes-2017.05.19.md`：**默认接口实现**（有体成员隐含 Overridable、NotOverridable 允许且隐含、允许 Implements；隐式接口实现暂定为错误）
  - `vblang\meetings\2017\vbldm-notes-2017.08.09.md`：**Await in Catch/Finally**（原则上批准，优先级依附 IAsyncDisposable/Async Using）；非尾部命名实参在后期绑定中报编译期错误
  - `vblang\meetings\2017\vbldm-notes-2017.08.23.md`：晚绑定现代化（Dynamic 伪类型、方法作用域 Option/Imports、`!` 成员访问、擦除接口）；Out 建议识别 OutAttribute 不新增修饰符
  - `vblang\meetings\2017\vbldm-notes-2017.08.30.md`：**可空引用类型推迟**（等 C# 原型；`!` 与 dict!key、radius! 冲突）
  - `vblang\meetings\2017\vbldm-notes-2017.10.18.md`：JSON 字面量（#101）、标注类型/标签字符串字面量（#184）、Guid 字面量（#27）、Try 赋值（#190，对照 Swift if let）
  - `vblang\meetings\2017\vbldm-notes-2017.11.15.md`：属性提案裁决（**Set 参数类型推断通过**；Static 属性变量、隐式后备字段被拒；先调研 Source Generators 再定 INPC 三提案）
  - `vblang\meetings\2017\vbldm-notes-2017.12.06.md`：Range `1 To 10 Step 2` 需写 speclet；多重 For 控制变量、`Exit For j`、泛型参数特性、refness 重载平局规则通过；**顶层语句推迟到 2018-01**；INPC #194 通过/#198 被拒/#107 推迟
- **2018（7 场）**：
  - `vblang\meetings\2018\vbldm-notes-2018.02.07.md`：可空引用类型继续观望；块级作用域 Option 留待（仅 Option Strict 有价值）；「任意已安装语言混用」判定不可行
  - `vblang\meetings\2018\vbldm-notes-2018.02.21.md`：`Private Protected` 保留现状（实现期已落地 IL 风格命名）；Await in Catch/Finally 因 GoTo/OnError 暂缓；C# 默认接口方法暂无动作
  - `vblang\meetings\2018\vbldm-notes-2018.02.28.md`：**Range 倾向走 API 支持而非新增 `..` 语法**；同意 **Await in Catch and Finally**（「Let's do it!」）；tuple 相等待深究
  - `vblang\meetings\2018\vbldm-notes-2018.03.21.md`：CInt/Fix → 原生 opcode 优化按「纯优化」处理；INotifyPropertyChanged 编译期改写方案
  - `vblang\meetings\2018\vbldm-notes-2018.05.30.md`：引入审阅标签；In/Out 运算符、`Return?`、add/removeHandler 空条件均 No Plans；Select TypeOf 归入模式匹配
  - `vblang\meetings\2018\vbldm-notes-2018.06.13.md`：**保守决策原则成文**（几乎不做 breaking change、保持 VB-like、C# 领跑 CLR/库层）；批量归档首批 No Plans/Rejected
  - `vblang\meetings\2018\vbldm-notes-2018.12.19.md`：**模式匹配**（issue #337；`Matches` 关键字、分阶段、目标 VB 16.2）——vblang 最后一篇会议笔记
- **README/模板**：`vblang\meetings\README.md`（LDM 笔记用途/风格，非逐字纪要）、`vblang\meetings\notes-template.md`、`vblang\meetings\2014\README.md`…`2018\README.md`（年份索引，一句一会议）

### 规范（spec）— Version 11
| 路径 | 一句话要点 | 关键词 |
|---|---|---|
| `vblang\spec\introduction.md` | 语言定位、强/松散双模、兼容与弃用策略、ANTLR 文法记号 | strong/loose typing, compatibility, deprecation, ANTLR |
| `vblang\spec\general-concepts.md` | 总纲：声明空间、遮蔽（Shadows/Overloads）、五种访问、泛型（约束/变型）；**ByVal/ByRef 不参与签名** | declaration space, shadowing, accessibility, Global, variance |
| `vblang\spec\lexical-grammar.md` | 词法：续行（显式 `_` + 隐式）、注释 `'`/REM、类型字符 % & @ ! # $、转义标识符 `[keyword]`、字面量（&H/&O、`"a"c`、日期 `#...#`） | line continuation, type character, escaped identifier, literals |
| `vblang\spec\types.md` | 值/引用类型、可空 `T?`（=System.Nullable(Of T)）、数组、委托、**标准模块**（成员隐式 Shared、不可实例化）、构造类型 | value/reference types, nullable, Module, constructed type |
| `vblang\spec\type-members.md` | 成员声明：Implements/Handles、Sub/Function/Declare、事件（隐式 XEventHandler/XEvent/add_/remove_）、属性（Get/Set/Default/自动）、运算符（须 Public Shared） | Implements, Handles, WithEvents, Property, Operator CType, Declare |
| `vblang\spec\statements.md` | 语句全集与执行语义；**结构化 + 非结构化异常并存不可混用**；Select Case 禁止 fall-through；`=` 赋值优先于相等 | Try/Catch/When, On Error, Resume, GoTo, ReDim Preserve, Using, Await, Yield |
| `vblang\spec\expressions.md` | 14 类表达式与重分类；全二元运算符左结合；`&` 拼接全转 String；`If(x,y)` 按需求值；Object 默认晚绑定 | reclassification, late-bound, TypeOf...Is, precedence, concatenation |
| `vblang\spec\overload-resolution.md` | 重载解析算法：类型推断、适用性六步、delegate 松弛、narrowing 消除、扩展方法处理、11 条 tie-breaker | applicability, delegate relaxation, specificity, tie-breaker, ParamArray |
| `vblang\spec\conversions.md` | widening/narrowing 定义；**Option Strict 决定隐式转换宽严**；`CType`/`DirectCast`/`TryCast` 分工；装箱值类型按值复制；用户定义转换 ≤3 步 | widening, narrowing, Option Strict, CType, boxing, nullable, dominant type |
| `vblang\spec\attributes.md` | `<...>` 属性块语法、AttributeUsage（AllowMultiple/Inherited）、属性实参限制 | attribute block, AttributeUsage, AttributeTargets, angle brackets |
| `vblang\spec\documentation-comments.md` | `'''` XML 文档注释；成员 ID 编码（E/F/M/N/P/T 前缀 + `@`/`~` 记号） | doc comment, cref, member ID, ID string |
| `vblang\spec\source-files-and-namespaces.md` | Option 语句、Imports/别名/XML 命名空间、Global、Main 入口签名、Module | Option Strict, Imports, Namespace, Main, Global |
| `vblang\spec\preprocessing-directives.md` | 条件编译 `#Const`/`#If`（不受 Option 影响）、`#ExternalSource`/`#ExternalChecksum`、`#Region` | conditional compilation, #If, #Const, #Region |

---

## 三、对 VBScript.NET 的含义 → 已移至 `decisions.md`

> 原「三、对 ModVB / VBScript.NET 的含义（M1–M8）」已按用户指示独立成文，见 **`decisions.md`**（VBScript.NET 设计决策记录）。该文件同时记录决策修正（ref struct 解法、NativeAOT 桥、postfix-casting 语义）。本索引只保留 VB 语言设计事实。

---

## 四、引用纪律提示

- 引用 VB 原文必须**逐字准确**并标注来源文件（用上述路径）；无法核实的标注 **Suspect** 或列入 **OPEN QUESTIONS**。
- 本索引中已核实可引用的原文与出处（供 meeting agent 直接使用）：
  - 「Nothing is more like default(T), and VB lacks null.」→ `vblang\meetings\2014\LDM-2014-02-17.md`（节 18，Null literal）。
  - 「We strongly believe that Visual Basic has a stance - a way of doing things. We will strive to maintain consistency with things being "VB-like" (often resulting in a No Plans or rejected label)」→ `vblang\meetings\2018\vbldm-notes-2018.06.13.md`（Review process）。
  - 「We will almost never make breaking changes to Visual Basic (often resulting in _Rejected_ label)」→ `vblang\meetings\2018\vbldm-notes-2018.06.13.md`（Review process）。
  - 「Consistent with the Visual Basic language strategy C# will take the lead on some issues - particularly those that would involve changes to the CLR or .NET libraries.」→ `vblang\meetings\2018\vbldm-notes-2018.06.13.md`（Review process）。
  - 「We all want to do pattern matching, it's the thing we are most excited about after C# interop issues. The time-frame we think is practical remains around VB.NET 16.2.」→ `vblang\meetings\2018\vbldm-notes-2018.12.19.md`（Agenda）。
  - 「The Visual Basic programming language can be either a strongly typed or a loosely typed language.」→ `vblang\spec\introduction.md`（正文首段）。
  - 「compatibility between versions must be preserved except when the benefit to language consumers is of a clear and overwhelming nature.」→ `vblang\spec\introduction.md`（Compatibility）。
  - 「No, never, not even for the ones that are clearly recognizable as constants. The rule is that string interpolation is semantically shorthand for String.Format (notwithstanding any under-the-hood compiler optimizations).」→ `vblang\meetings\2014\LDM-2014-04-02.md`（Q. Const?）。
  - 「We introduced a new attribute, `System.Runtime.CompilerServices.OverloadResolutionPriority`, that can be used by API authors to adjust the relative priority of overloads within a single type as a means of steering API consumers to use specific APIs…」→ `vblang\proposals\overload-resolution-priority.md`（Summary）。
- **OPEN QUESTIONS**（本索引未深挖、需自行核实的点）：VB 16 顶层语句与 Range 的最终落地状态（本库 meetings/ 止于 2018，提案层更新稀疏——最近一次为 2024 年 Overload Resolution Priority；后续状态需查 GitHub issues）；可空引用类型在 VB 的最终方案（2017-2018 仅观望）；`Matches` 模式匹配后续进展（目标 VB 16.2，本库无正文）；Proposal-DerestrictedOperators 与 Implicit-default-optional-parameters 的最终采纳状态。
