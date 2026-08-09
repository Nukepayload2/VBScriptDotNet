# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周处理声明式编程与代码生成章节的第二份建议（`Replaceable`/`Replaces`/`MustReplace`/`NotReplaceable`）。与前一份智能属性建议（13.1）不同，这份建议把宝押在源码生成器生态上——而我们恰好有一份 2017 年的主线裁决可以回看：`#107 Replaceable Members` 当时被 **Deferred to next release**，理由正是"源码生成器离得太远"。现在是 2026 年，生成器早已是主流工具链的一部分，所以"推迟"的前提条件已经消失。但我们会看到，那份裁决背后的谨慎——"不能仓促推出完整的元编程方案"——仍然有效，只是换了一种方式成立。

## Agenda

* [Proposal: 替换修饰符（Replaceable / Replaces / MustReplace / NotReplaceable）](#proposal-替换修饰符)

## Proposal: 替换修饰符

_Related: [vblang #107 – Replaceable Members](https://github.com/dotnet/vblang/issues/107)；[vblang #219 – Implementing INotifyPropertyChanged is Tedious](https://github.com/dotnet/vblang/issues/219)；ModVB：`proposal-smart-attributes.md`（13.1）、`proposal-partial-members.md`（13.3）_

### 场景与缺口

提案的出发点我们完全认同：源码生成器到今天仍然只会"新增成员"，它无法**接管**一个由人书写的算法，也无法安全地让人在生成样板之上做局部微调。人写一个语义自然的 `ToCommaSeparated`，工具想提供一个数组免枚举器快路径——今天这件事必须靠手抄一份不同名字的成员，然后让调用方改指向；只要中间隔了一层调用方，优化就变得脆弱。

```vb
' 今天：工具想接管这段算法，只能另起炉灶。
Function ToCommaSeparated(items As IEnumerable(Of Object)) As String
    ...
End Function

' 然后工具版本必须换名字或换签名，所有调用方一起改。
' 更糟：一旦人微调了工具版本，工具下一次生成时无从得知，
' 只能粗暴覆盖或放弃更新——"对话"根本没有建立。
```

Anthony 原文 13.2 用一句话点题："This is approach is all about conversational collaboration between an end-developer and source generation tools."——人写语义自然的算法，工具为性能优化它；工具提供默认实现，人只微调需要的部分；人声明需求，工具补全、工具再声明需求，人再补全（`MustReplace`）。这个"对话式往返"的愿景是真实的，而且它恰好补上源码生成器生态里一块真实的空白：**生成器之间的契约、生成器与人的契约，目前没有任何编译期保证**。

我们还有一个必须正视的历史包袱：主线 2017 年已经看过这份设计的近亲。`#107 Replaceable Members` 作为 `#219 INotifyPropertyChanged` 场景中最一般的方案，在 2017.11.15 讨论时是"我们不知道源码生成器的状态，应该调研并在未来会议带回发现"（"We don't know the status of this and source generators. We should investigate and come back with findings in future meeting."），同年 12.06 落定为 **"Deferred to next release"**。当时对源码生成器的完整判断是："it still feels like a _long_ lead to get it right both in design and implementation and especially iteration with the community for feedback. We absolutely cannot rush full meta-programming solution and our plate is pretty full already with HUGE ticket items for the next major version." —— 这句"绝对不能在元编程解决方案上仓促行事"，我们认为今天依然成立，哪怕生成器本身已经成熟。

### 候选方案

**PROPOSAL A — 完整四修饰符。** `Replaceable`/`Replaces`/`MustReplace`/`NotReplaceable`，与 `Overridable`/`Overrides`/`MustOverride`/`NotOverridable` 继承体系逐一平行，覆盖三种协作模式（Human-to-Tool、Human-to-Tool-to-Human、双向补全）。这是 Anthony 13.2 的完整形态，也是提案正文的形态。

**PROPOSAL B — 三修饰符，去 `NotReplaceable`。** 只保留 `Replaceable`/`Replaces`/`MustReplace`。`NotReplaceable` 在 Anthony 原文里自己说了"只在某些声明默认就可替换时才有用"（"Only useful if the final design includes that certain kinds of declarations are replaceable by default"）；若不采纳"默认可替换"，这个修饰符没有存在对象。

**PROPOSAL C — 只读双向，去 `MustReplace`。** 只做"工具生成默认、人微调"（`Replaceable` + `Replaces`），不做 `MustReplace` 的编译期补全契约。对应提案 Alternatives 第二项。

**PROPOSAL D — 零新语言机制。** `Replaceable`/`Replaces` 的人-工具微调场景，用**既有 partial methods + 工具侧签名探测**覆盖：生成器每次运行前检查"人类是否已声明同名同签名成员"，有则不生成默认实现；`MustReplace` 降级为约定 + analyzer 提示。对应提案 Alternatives 第一、四项，也是 C# 生成器生态今天的主流做法。

**PROPOSAL E — 只保留 `MustReplace` 作为唯一新原语。** 把"此成员必须由另一个源码声明补全"做成编译期契约；把"人微调生成默认"划给 13.3 Partial 成员扩展与工具侧探测。这是我们在会议中自己构造的最小种子。

### 权衡：Q&A

- **继承平行是否成立？这是全场第一个分歧点，也是最大的分歧点。** `Overrides` 是**类型层次**里的虚方法分派：基类与派生类各有独立的方法槽，运行期通过 vtable 决定调用哪个实现。`Replaces` 是**同一类型内部**的重复解析：两个同签名声明拼在同一个类里，编译期选一个胜出。把后者命名为"类似 Overrides"会暗示一套运行期语义，而它实际上**零运行期语义**——选中的成员就是一个普通方法。We think 这个平行是把一份设计建立在错误的类比上：它真正的祖先不是虚方法，而是 VB 规范里已经存在的 **partial methods**——"两个同签名声明，一个供体，属性合并"的全部机制在那里早已写好（详见下一条）。一旦换上正确的类比，`NotReplaceable` 的"类似 NotOverridable"也就站不住了——2017.05.19 的修饰符表里记录过 "It's not permitted to make a NotOverridable Overrides. Means 'non virtual'"，这个约束在替换语境下没有对应物，除非引入默认可替换。

- **`Replaces` 与 partial methods 的边界在哪？** VB 规范 §Partial Methods 明确写了："The partial method declaration must be declared as `Private` and must always be a subroutine with no statements in its body." 以及 "Only one method can supply a body to a partial method. A method supplying a body to a partial method must have the same signature as the partial method, the same constraints on any type parameters, the same declaration modifiers, and the same parameter and type parameter names. Attributes on the partial method and the method that supplies its body are merged, as are any attributes on the methods' parameters." —— 也就是说，**"两个同签名声明、其中一个供体、属性合并"在 VB 里不是新概念**。`Replaceable`/`Replaces` 与 partial methods 的差异只有三处：成员种类从 Private Sub 扩展到任意成员、可见性不再锁死 Private、以及 `MustReplace` 把"可选供体"改成"强制供体"。前两处差异正是 13.3 Partial 成员的提案内容。所以 `Replaceable`/`Replaces` 实质上是在**抢 13.3 的地盘**，还多造了一整套命名。We think 这是对设计原则 #3（不引入第二种做事方式）最直接的触犯。

- **被替换的 `Replaceable` 函数体是否仍须编译？** 必须。理由：① 它是"语义自然的规格"，若因一个 bug 被工具版本掩盖，人永远不会发现规格错了；② 生成器与人可能在不同构建间交替，任何一方把坏代码推进仓库，另一方的输出就失去意义。但"两个函数体都要编译"意味着同一逻辑要维护两份正确性，且 IDE 要标出"此声明已被替换、仅作规格"。这是一个真实的设计摩擦，提案完全没提。

- **`MustReplace` 是不是重新发明了抽象成员？** 表面上像 `MustOverride`，但 `MustOverride` 的供体在**派生类**里，`MustReplace` 的供体在**同一个类的另一个声明**里。它更像"反向 partial methods"：partial methods 是"可选供体，缺省调用被忽略"，`MustReplace` 是"强制供体，缺省即编译错误"。这个"编译期保证的补全契约"是我们全场唯一真正心动的地方——但它的实现仍然要建立在"同类型双声明 + effective member"的地基上（见追问 #3）。

- **谁负责验证契约？** 编译器。我们构想了干净的三态规则：

| 声明组合 | 结果 |
|---|---|
| `Replaceable` 且无 `Replaces` | 合法，`Replaceable` 本身生效 |
| `Replaceable` + `Replaces` | 合法，`Replaces` 生效，`Replaceable` 作废 |
| `MustReplace` 且无 `Replaces` | 编译错误（契约未履行） |
| `Replaces` 且无 `Replaceable`/`MustReplace` 目标 | 编译错误（孤儿替换） |

- **工具侧探测能不能覆盖 C？** 大部分能。生成器在发出默认实现前检查人类是否已声明同名同签名成员，是现代生成器的标准做法（MVVM Toolkit、protobuf、JSON 生成器都这么做），它能覆盖"人微调生成默认"（Human-to-Tool-to-Human）而不需要任何语言改动。它覆盖不了的只有：编译期强制（无 `Replaces` 即报错）与确定性的胜者语义（不再依赖生成器内部的"先到先得"或探测顺序）。这两个漏网之鱼恰恰是 `MustReplace` 的专利。

- **默认可替换要不要做？** 不要。`NotReplaceable` 的整个存在理由是"某些声明默认就可替换"——而一旦自动实现属性、事件默认可替换，任何生成器都能静默改写既有代码的绑定，这是设计原则 #1（永不破坏现有代码）的红线。We think 这是 Anthony 设计里最危险的隐含项：`NotReplaceable` 修饰符像一扇安全门，但门的存在暗示房子里有龙。**不引入默认可替换，龙就不存在，门也不需要。**

- **与 13.1 智能属性的切片怎么划？** 智能属性处理"属性读写时的行为注入"（Trim/Idempotent/AutoRound 那类），是**按属性**的横向切片；替换修饰符处理"整个成员的实现替换"，是**按成员**的纵向切片。两者方向不同，不冲突，但提案把 13.1 列为替代方案而不说明切片划分，读者会以为它们是同一个问题的两个答案。它们其实是互补的：同一份代码里，属性上的行为用 13.1，方法级的契约用 `MustReplace`——如果后者能活下来的话。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`Replaceable`/`Replaces`/`MustReplace`/`NotReplaceable` 目前都是合法标识符，所以它们必须是**上下文关键字**，只在成员修饰符位置有意义。VB 已有上下文关键字先例（如 `Partial`），解析器层面无歧义。真正的文法问题是 body 判定：`MustReplace Function Foo() As Integer` 没有 `End Function`（类比 `MustOverride`），而 `Replaceable`/`Replaces` 必须有完整 body。三者的 body 有无必须由 binder 而非 parser 强制，且要与"`Partial` 成员可无 body"（13.3 提案）区分——语法上 `MustReplace` 与 `Partial` 成员的无 body 形态在 token 流里长得一样，必须靠语义区分。

#### 2. 角案例与边界语义

- **`Overloads` 与多重 `Replaces`**：同一个 `MustReplace` 契约被两个工具（或工具+人）同时 `Replaces`，谁胜出？我们的答案：编译错误，与 `Overloads` 冲突同类处理。但注意——工具 A 和人可能各自生成同签名 `Replaces`，编译器必须给出"重复替换"错误并点名两个文件，否则"对话"会变成"抢答"。这是提案未决问题之一，我们给出方向。
- **被替换声明上的额外子句**：`Replaces Shared Function ToXml(...)` 若还带 `Implements IXmlSerializable`、`Handles` 或 `Overrides`，这些子句挂在哪个声明上？partial methods 先例是 Handles 列表合并；但 `Implements`/`Overrides` 合并规则没有先例，且牵涉接口分派——v1 应要求这些子句**只允许出现在 effective 声明（`Replaces`）上**，`Replaceable` 上的视为错误。
- **泛型与 `Shared`**：签名匹配必须包含类型参数个数与约束（partial methods 先例已要求"same constraints"）；`Shared`/实例性必须一致（ToXml 的 `Shared` 示例确认了这个维度存在）。
- **`Async`/`Iterator`**：签名匹配含返回类型，所以 `Async Function` 与同步 `Function` 返回类型不同、必然不匹配；但 `Iterator` 的返回类型仍是 `IEnumerable(Of T)`，可能被一个非 `Iterator` 的同签名 `Replaces` 顶掉——需要规则：`Iterator` 性与 `Async` 性视为签名的一部分，防止工具悄悄改变人写的语义。

#### 3. 作用域与绑定

这是实现成本的核心，也是我们最终判定性价比的地方。今天，同一类型内两个同签名成员是重复声明错误。本特性要让它合法，意味着符号表必须支持"**一个逻辑成员，多个声明，一个 effective 声明**"。后果链很长：

- `GetTypeInfo` / 绑定：调用点绑到 effective 声明；语义模型里 `Replaces` 与 `Replaceable` 是两个 `MethodSymbol` 还是一个？若是两个，分析器与生成器必须理解"重复符号"；若是一个，需要新的"declaration → effective"间接层。
- **"源码符号 = 元数据符号"不变量**：Roslyn 的 semantic model、分析器、生成器都假设源码里的一个成员对应元数据里的一个方法。effective 概念打破的是这个编译器级地基假设，不是普通特性。这与我们此前在 async-sub 会议上对 `Flush`/`FlushAsync` 的担心同源。
- 我们 v1 的倾向：保留两个符号，但 `GetTypeInfo` 一律返回 effective；`Replaceable` 声明在语义模型里标记为"被替换、不可见"状态，IDE 可据此画删除线。这是可行的，但必须写进 spec 并配原型验证。

#### 4. 与既有特性的交互

- **`Partial` 类型**：VB 规范 §Partial types 明确"至少一个声明要有 `Partial` 修饰符，且允许把可见声明上的 `Partial` 省略、只标在隐藏声明上"。提案 Step 3 的 `Class AthleticsTeam`（无 `Partial`）+ 工具文件的 `Partial Class AthleticsTeam` 正好落在规范明确祝福的形态里，这一点没有问题——但 `Replaces`/`Replaceable` 必须**跨 partial 声明**合法，这是它们存在的先决条件，提案没有点明这个依赖。
- **partial methods**：见 Q&A 第二条。这是本特性最亲的邻居，提案完全没提，是最大的遗漏。
- **`Overridable` + `Replaceable` 组合**：一个成员可以同时"可被派生类 override"且"可被同类型替换"吗？逻辑上两轴正交，但组合会产生 2×2 的语义矩阵（基类 Replaceable、派生类 Overrides、同类型 Replaces…），v1 应**禁止组合**，把交叉留到有真实需求时。
- **Option Strict Off / late binding**：`Replaceable` 人写版本在宽松模式下晚期绑定，`Replaces` 工具版本早期绑定——两个声明各自在自己的文件选项下编译，互不污染，这反而是本特性少有的"分叉自然健康"处。

#### 5. Breaking change 与兼容性

特性本身 opt-in，新修饰符不进则旧代码零变化——**除非**采纳"默认可替换"（自动实现属性、事件默认 `Replaceable`）。一旦默认可替换，生成器发出 `Replaces Property Foo` 就可能把此前绑定到自动属性 backing field 的代码改绑，这是教科书级的隐蔽语义变化（原则 #7，`Return?` 被拒的同类理由）。我们的裁定：**默认可替换连同 `NotReplaceable` 一起砍掉**，breaking change 面归零。剩余的 breaking 风险只剩"新关键字作为上下文关键字是否与既有标识符冲突"——由于是上下文关键字，不冲突。

#### 6. Option Strict / 编译选项分叉

两条路径行为应一致：契约验证（缺 `Replaces` 报错、孤儿 `Replaces` 报错）与 effective 解析不依赖 Option 设置。宽松路径下，`Replaceable` 函数体的 late-bound 调用照常编译；它被替换后那段代码不再生效，但**仍须编译通过**——这会让宽松模式下一些"只有宽松才能编译"的 `Replaceable` 函数体成为长期存在的编译负担。我们 v1 接受这个负担（安全大于整洁）。

#### 7. IDE / IntelliSense

需要三种新可视化状态：`Replaceable` 声明被替换（删除线 + "由 ToolGenerated.vb 提供"提示）、`MustReplace` 契约未履行（红色波浪线 + "缺少 Replaces 实现"）、孤儿 `Replaces`（错误列表 + 建议目标）。调用点的 Go-to-Definition 指向 effective 声明。生成器输出文件在解决方案里可见，导航链路尚可，但"替换状态"的展示没有任何先例，必须原型验证——不做进规范等于没设计。

#### 8. 数据 / 普遍性

诚实地说：主流生成器生态今天**已经用工具侧探测 + partial methods 投票**解决了"人微调生成默认"，它们没有等语言特性。真正没有先例的空白只有 `MustReplace` 的**双向契约**——"工具声明一个需要、人（或另一个工具）必须补全"——这正是 StoredProcedure 示例演示的、也是 2017 年 `#107` 讨论里主线明确预判的方向（"if we had `Replaces` w/ source generators we wouldn't do `Bindable` or `WithPropertyEvents` as `INotifyPropertyChanged` is basically the poster child for the source generator feature"）。但我们没有任何数据证明"工具需要人补全成员"这个方向的高频程度。`Suspect`：StoredProcedure 是唯一的演示，说服力来自直觉而非证据。

#### 9. 更简替代

1. **工具侧签名探测**（PROPOSAL D）：覆盖 Human-to-Tool-to-Human，零语言改动，生态已验证。
2. **扩展 partial members**（13.3 Partial 成员）：`Replaceable`/`Replaces` 去掉 Private/Sub 限制后就是它；属性/事件/构造器级的"双声明一供体"可以统一由 13.3 承载。
3. **`MustReplace` 单独成原语**（PROPOSAL E）：在 partial members 地基上加"强制供体"契约，这是三个替代里唯一语言必须亲自做的事。
4. 什么都不做：生成器继续用探测 + 约定，代价是"契约无编译期保证、重命名静默失效"——这正是提案 Drawbacks 自己承认的脆弱点。

#### 10. 成本 / 优先级

- 符号表 effective-member 概念：编译器地基级，涉 binding、semantic model、IDE、分析器。这是大头。
- 上下文关键字 ×4 + 文法 + 契约验证：中等。
- 若走 PROPOSAL E（`MustReplace` 建在 13.3 之上），大头成本大部分由 13.3 承担，`MustReplace` 只剩"强制供体 + 缺省报错"的增量。
- 2017 年"绝对不能在元编程上仓促"的告诫，转化为今天的行动准则：**不建平行系统，只加最小契约**。

#### 11. 运行时 / CLR 硬约束

无新 IL，无 PEVerify 问题，不触达 CLR 存储规则。effective 成员编译为普通方法；`nameof`/反射/表达式树看到的都是 effective 成员。被丢弃的 `Replaceable` 函数体不发射（其属性按 partial-methods 先例与 effective 合并）。唯一的运行时相关问题是"`Replaceable` 函数体里的 `Handles`/`Implements` 子句若被丢弃是否残留元数据"——v1 规则（子句只许在 `Replaces` 上）已经规避。

#### 12. 值不值得做

- 价值：愿景真实，但 80% 可被工具侧探测 + 扩展 partial members 收割；独有的 20%（`MustReplace` 编译期契约）方向对、证据缺。
- 成本：A/B 全设计的符号表成本高；E 的成本可接受。
- 风险：默认可替换是地雷（已砍）；平行系统是原则 #3 的持续债务（已转嫁 13.3）。
- 判定：**完整设计不值，最小契约值得一看。** 若把 A 当成品接受，我们会反对；若把 E 当种子设计，我们愿意出原型。

### VB 基因对照

- **消除常见样板（原则 #9）**：ToXml / StoredProcedure 场景确实是样板消除的教科书。方向正。
- **不引入"第二种做事方式"（原则 #3）**：**扣分最重的一项**。替换修饰符与继承体系平行（`Overridable`/`Overrides` 已经存在）、与 partial methods 重叠（VB 规范已有"双声明一供体"机制）。造第三个做事方式，是这条原则明确反对的。只有 `MustReplace` 的"强制供体契约"是现有机制没有的能力，不构成"第二种"。
- **读起来像英语（原则 #5）**：`Replaceable`/`Replaces`/`MustReplace` 读感极佳，与 `Overridable` 家族同构，这是 Anthony 设计里最 VB 的部分。
- **永不破坏（原则 #1）**：砍掉默认可替换后合规；保留则触线。文档未自省这一点。
- **冗长只在有用时是美德（原则 #10）**：四个修饰符是过度仪式；`MustReplace` 一个字不浪费。
- **与主线关系（对照表 2.3）**：`Replaceable`/`Replaces` 主线状态是"推迟"（2017.12.06 `#107` Deferred to next release），Anthony 是"完整设计"——方向一致、范围更激进。我们今天的裁决实际上是把主线那个"推迟"的条件（生成器状态）重新打开，然后给出一个比 Anthony 更窄的落点：**不是四修饰符，而是把地基让给 13.3，只留 `MustReplace`**。

### RESOLUTION:

1. **拒绝"继承平行"的框架**。`Replaces` ≠ `Overrides`：前者是同一类型内的编译期重复解析，后者是类型层次里的运行期虚分派。正确的祖先概念是 VB 规范 §Partial Methods。这个重新类比推翻了 A/C 两案里靠对称性得来的论证，也让 `NotReplaceable` 失去立足点。
2. **完整四修饰符（PROPOSAL A）否决留档**；`NotReplaceable`（PROPOSAL B）随"默认可替换"一起砍掉——不引入默认可替换，破坏性变更面归零。
3. **`Replaceable`/`Replaces` 的"人微调生成默认"场景交给工具侧签名探测（PROPOSAL D）与 13.3 Partial 成员扩展**，不在本建议内建立平行系统。与 `proposal-partial-members.md` 团队对表：把"任意成员双声明一供体 + 属性合并"作为 13.3 的交付物，本建议引用之。
4. **`MustReplace` 单独保留为候选原语（PROPOSAL E）**：建在 partial members 地基上的"强制供体"契约；缺供体即编译错误、孤儿 `Replaces` 即编译错误；`Handles`/`Implements`/`Overrides` 子句只允许出现在 effective 声明上；`Async`/`Iterator` 性视为签名的一部分。
5. **契约验证规则采用三态表**（见 Q&A）：`Replaceable` 无 `Replaces` 合法、`Replaceable`+`Replaces` 后者生效、`MustReplace` 无 `Replaces` 报错、孤儿 `Replaces` 报错。
6. **被替换的函数体仍须编译**。`Replaceable` 体是规格，不是草稿；两条路径（Option Strict On/Off）下都成立。
7. **符号表 effective-member 概念是唯一的大额实现成本**，列为原型前置条件：语义模型保留两个符号、`GetTypeInfo` 返回 effective、`Replaceable` 声明标记"已替换"状态。
8. **对主线 `#107` 的回应**：2017 年因生成器未成熟而推迟的前提已消失，重开讨论是有据的；但当年"不仓促做完整元编程方案"的告诫仍成立——我们以"最小契约 + 复用 13.3"回应，而不是把整套四修饰符直接送审。

### Implication:

- 与 13.3 Partial 成员团队联合起草 speclet：任意成员 partial 化的"双声明一供体 + 合并规则"，作为 `Replaceable`/`Replaces` 与 `MustReplace` 的共同地基。
- 为 `MustReplace` 写最小原型：上下文关键字、契约验证（三态表）、符号表 effective 标记、缺省报错文案；用 StoredProcedure 双向示例做端到端演示（生成器发出 `Replaces` 响应人的 `MustReplace`，首次构建即成功）。
- 修复提案示例质量问题（见 OPEN QUESTIONS）：`ToCommaSeparated` 工具版本的越界 bug、XML 示例对未声明语法（XML 字面量内 `Yield` 循环、`{...}` 插值）的隐式依赖。
- 补一份 Compatibility 分析：默认可替换的破坏性推演（即使已砍，也要留档为什么砍）、上下文关键字与既有标识符的兼容、partial 类型跨声明替换的既有规范依据。
- 调研"工具声明需要人补全"方向的数据：与 MVVM Toolkit / protobuf 作者对表，确认 `MustReplace` 是否有真实高频需求，还是 StoredProcedure 式演示的孤例。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：同类型 2×2 组合（`Replaceable` + `Overridable`、`Replaces` + `Overrides`）是否 v1 全禁？我们倾向全禁，但需要 13.3 合并规则定稿后复核。
- `OPEN QUESTIONS`：`MustReplace` 契约的可见性语义——被要求补全的成员默认 `Public` 还是继承 `MustReplace` 声明所在类型的默认可见性？`Replaces` 的可见性与目标不一致时，取 effective 还是报错？
- `OPEN QUESTIONS`：生成器跨构建的契约持久性——工具 A 在某次构建发出 `MustReplace`，之后工具 A 被移除，残留的未履行契约如何诊断（错误信息应点名"此契约由已移除的生成器发出"）？
- `OPEN QUESTIONS`：签名匹配是否需要显式"匹配键"（attribute/key）来防重命名破坏——提案 Unresolved 第 4 项；我们倾向 v1 不做（依赖签名匹配 + 错误可见性），但把选项留档。
- `TODO`：修复 `ToCommaSeparated` 示例（`For i = 1 To builder.Length - 1` 应为 `To items.Length - 1`，现版本对 2+ 元素数组会越界或漏项）。
- `TODO`：为 XML 示例补"依赖的 XML 字面量增强提案"标注，或改写为可编译形式。
- `Follow-up`：与 `meeting-smart-attributes` 团队对表，确认属性级（13.1）与方法级（`MustReplace`）的切片划分写入两案正文。

### 状态

- **LDM 状态：** `Replaceable`/`Replaces`/`NotReplaceable` 为 No Plans（并入 13.3 + 工具侧）；`MustReplace` 为 LDM Considering。
- **三态判定：Table（完整建议 A）**——价值真实但载体应重构为"扩展 partial members + 工具侧探测"；**Consider（`MustReplace` 种子）**——需原型与需求数据；不因愿景而直接给 Active，这是对 2017 年那份"不仓促"的尊敬。

---

## 附录：特性评价

# 建议评价报告：proposal-replacement-modifiers.md

## 评价对象

- 建议：proposal-replacement-modifiers.md — `Replaceable`/`Replaces`/`MustReplace`/`NotReplaceable` 四替换修饰符
- 来源：Anthony 原文 13.2 "New Replacement Modifiers – Declarative w/ Source Generators"（`..\AnthonyDesign_wordpress.txt` L2007–2290；Human-to-Tool `ToCommaSeparated`、Human-to-Tool-to-Human `ToXml`、双向 `StoredProcedure`、`NotReplaceable` 全部出自该节）
- 配方目标：人与源码生成工具在同一源码成员上"声明-替换"协作——人写语义自然算法、工具优化；工具生成默认、人微调；`MustReplace` 提供编译期补全契约

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。三种协作模式的 Motivation 清晰、示例可演示（StoredProcedure 双向契约最具说服力）；但旗舰 `ToCommaSeparated` 工具版本有真实越界 bug、XML 示例依赖未声明的增强语法、`Replaceable` 被替换后函数体语义（是否仍须编译）完全未定 | 已检查（无原型，效果封顶 3–4） | 无原型/运行结果；示例正确性存疑；核心语义（被替换体的处理）未定型，按"未决问题 ≥4 项封顶"规则效果最多 3–4 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。命名高度 VB（与 `Overridable` 家族同构，读起来像英语）；但把四修饰符打包、与继承体系平行又部分重叠于既有 partial methods，触犯原则 #3（第二种做事方式）；`NotReplaceable` 依赖"默认可替换"这一未论证的隐含项 | 已检查 | 平行系统债务；`NotReplaceable` 无独立存在对象；"继承平行"类比误导（实为同类型重复解析） |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊"。六章节模板齐全、示例与原文逐字一致、4 个未决问题具体诚实（如实列出是加分）；但缺文法（BNF）、缺兼容性分析、缺与 partial methods 的交互（最相关的既有机制！）、状态行是占位链接（`PROTOTYPE_OWNER/...`、`pr/1`）；旗舰示例有 bug | 已检查 | 无 spec/文法；无 Compat 分析；未提 partial methods 先例；`ToCommaSeparated` 越界、XML 示例语法未标注依赖；来源（13.2）未在文内标注 |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。雷（加速人-工具协作迭代）与水（盘活声明式编程、强化业务应用叙事）正向；风（演化一致性：平行系统 + 与 13.3 重叠）与暗（符号表地基改动、默认可替换的破坏风险）负向且未对冲 | 已检查（预测待定，须"已采纳"后定） | 文档未权衡"生成器生态已用工具侧+partial methods 投票"这条竞争路径；默认可替换=红线未自省 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。主材 = Anthony 13.2（未在文内标注章节号）；VB 基因 = 继承体系修饰符（`Overridable` 家族）与 P/Invoke `Declare`；隐含借鉴 C# 源码生成器生态未声明；与 13.1/13.3 的切片关系未标注 | 已检查 | 未标 13.2 来源；未声明"生成器生态 = C# 主线惯例"；未标注对 partial methods 既有规范的复用/偏离 |

## 设计原则对照

- **与 VB 基因：部分一致、关键偏离。** 命名与可读性（原则 #2/#5）高度一致；消除样板（#9）方向正；但**原则 #3（不引入第二种做事方式）被正面触犯**——替换体系与继承体系平行、又重叠于既有 partial methods；原则 #1（永不破坏）在"默认可替换"隐含项下触线；原则 #10（冗长仅在有用时）被四修饰符的仪式感拖累。
- **与主线关系：主线一致、Anthony 更激进（2.3 对照表"推迟 / 完整设计 / 一致"成立）。** 主线 `#107` 2017 年 Deferred to next release，前提是生成器未成熟；本建议在生成器成熟后给出完整设计，方向与主线预判一致（主线自己就说"若有了 `Replaces` + 生成器，就不做 Bindable/WithPropertyEvents"）。但本建议与 13.3 Partial 成员**内部冲突**（抢同一块编译器地基），需要划界。
- **破坏性变更：无（opt-in）**，但**潜在有**——若采纳"默认可替换"，生成器可静默改写既有绑定（自动实现属性/事件），这是隐蔽语义变化红线。本建议文档未分析。

## 总评

- **达成程度：部分达成。** 愿景与主线历史判断一致，`MustReplace` 契约是真实的新能力；但完整四修饰符设计与既有机制（partial methods + 生成器工具侧 + 13.3）大面积重叠，且旗舰示例正确性存疑。
- **LDM 三态建议：完整建议 Table；`MustReplace` 种子 Consider。** 完整四修饰符应重构：`Replaceable`/`Replaces` 划给 13.3 Partial 成员扩展 + 工具侧签名探测，`NotReplaceable` 随"默认可替换"一起砍掉；只保留 `MustReplace` 作为"扩展 partial members + 强制供体契约"的最小原语，配原型与需求数据后再议 Active。
- **主要问题：** ① "继承平行"类比误导，机制实为同类型重复解析，真正祖先概念是 partial methods；② 与 13.3 抢地基、与生成器工具侧探测重叠，未做更简替代分析；③ 旗舰 `ToCommaSeparated` 示例有越界 bug、XML 示例依赖未声明的增强语法；④ 符号表 effective-member 概念的地基成本未评估；⑤ "默认可替换"隐含破坏性风险未自省。

## 返工建议

- **补充章节**：文法（BNF 中 modifier 列表、四上下文关键字的解析与 body 有无判定）；与 VB 规范 §Partial Methods 的差异表（成员种类、可见性、供体强制性的三步扩展）；effective-member 语义模型规则（两个符号 + `GetTypeInfo` 返回 effective，复用 partial methods 的属性/Handles 合并先例）；兼容性分析（"默认可替换"破坏性推演、上下文关键字与既有标识符兼容）。
- **补充证据**：修复 `ToCommaSeparated`（`For i = 1 To items.Length - 1`）并附基准；XML 示例标注所依赖的 XML 字面量增强提案或改写为可编译形式；`MustReplace` 双向契约的最小原型（生成器响应 `MustReplace` 的端到端演示）；"工具声明需要人补全"方向的需求数据（与生成器生态作者对表）。
- **未决问题处理**：继承规则 → v1 禁止跨类 `Replaces`，交叉语义（`Replaces` + `Overrides`）全禁并留档；多重 `Replaces` → 编译错误（同 `Overloads` 冲突）；`NotReplaceable` → 删除（随"默认可替换"一起否决）；签名匹配 → v1 用签名匹配、显式匹配键留档。
- **方向调整**：改写为"扩展 partial members（13.3）+ `MustReplace` 契约原语"的组合建议，明确与工具侧探测的边界，并在正文标注 Anthony 13.2 章节来源。

---

## 附录：C# 生态与互操作考量

> 本附录评估本提案（`Replaceable`/`Replaces`/`MustReplace`/`NotReplaceable`）与 dotnet/csharplang 现实方向的对应关系。结论先行：本提案的互操作面**几乎全在工具链生态层（源码生成器 + partial 成员），不在运行时/CLR 层**——effective 成员编译为普通方法、零新 IL、零新元数据；唯一的元数据交集是任务提示点到的「隐藏/覆盖」维度（`new`/`sealed override` 的 newslot/final），而那条轴与本提案**正交**。C# 原文均已逐字核实，来源为 `..\..\csharplang`；背景浓缩见 `..\..\csharplang-index.md`。

### 相关 C# 现实方向

**R1 源码生成器 = C# 工具链主流（索引 T6）。** C# 9 Source Generators + C# 10 Incremental Generators 把「生成代码」从运行时反射移到编译期；而生成器的边界与本提案的缺口判断完全一致——`proposals\csharp-9.0\extending-partial-methods.md` 明言「Given that the compiler doesn't allow generators to modify code hooking up this pattern would be pretty much impossible for generators.」也就是说，C# 编译器**不允许生成器修改既有代码**，人写语义自然的算法、工具想接管它——这个空白在 C# 侧同样真实存在。

**R2 partial 成员是 C# 指定的「声明/实现分离」载体，C# 不造平行系统。** C# 3 有 partial methods；C# 9 扩展其签名限制（显式访问性、非 void、`out` 参数）以服务生成器——`extending-partial-methods.md` Summary：「The goal being to expand the set of scenarios in which these methods can work with source generators as well as being a more general declaration form for C# methods.」C# 13 加 partial properties，C# 14 再扩到事件与构造器——`proposals\csharp-14.0\partial-events-and-constructors.md`：「C# already supports partial methods, properties, and indexers. Partial events and constructors are missing.」方向是**按成员种类逐个扩 partial**，而不是发明一套「替换修饰符」。且 C# 9 扩展的关键语义与本提案的 `MustReplace` 高度同构——「When a `partial` method has an explicit accessibility modifier the language will require that the declaration has a matching definition even when the accessibility is `private`」：**「有声明就必须有定义」的编译期契约在 C# 已存在**。

**R3 `[OverloadResolutionPriority]`（C# 13）= C# 对「同类型内同名成员谁胜出」的属性式答案。** `proposals\csharp-13.0\overload-resolution-priority.md` 原文：「We introduce a new attribute, `System.Runtime.CompilerServices.OverloadResolutionPriority`, that can be used by API authors to adjust the relative priority of overloads within a single type as a means of steering API consumers to use specific APIs, even if those APIs would normally be considered ambiguous or otherwise not be chosen by C#'s overload resolution rules.」它用**属性**而非修饰符调整编译期优先序，不发射新方法槽——是本提案「effective 成员胜出」概念在 C# 最近的亲戚，但范围仅限**重载**（同签名在 C# 不可能重复声明），且优先序是 C# 编译器单方语义，不跨语言生效。

**R4 继承层遮蔽/覆盖的元数据维度：`new` 与 `sealed override`（newslot/final）。** 这是「替换一个成员」唯一带元数据脚印的维度。csharplang 在 CLR 协作会议里确认过 `newslot` 位的语义——`meetings\2017\CLR-2017-03-23.md`：「All override declarations should omit the `newslot` bit to ensure no new vtable slot is allocated.」C# `new` 隐藏继承成员 → 方法槽设 **NewSlot**（不复用基类槽）；`sealed override` → **Final**（不许再 override）+ 复用槽。VB `Shadows`/`NotOverridable` 映射到同一对位。**本提案的 `Replaces` 是同一类型内的编译期重复解析，零运行期、零元数据——不共用这条轴。**

### 现实 vs 提案

| 维度 | 判定 | 理由 |
|---|---|---|
| 「生成器无法接管人写的成员」的缺口观察 | **兼容** | 与 R1 的 C# 原文逐字一致；本提案 Motivation 建立在真实观察上，不是 VB 独有的幻觉 |
| `Replaceable`/`Replaces` 的载体 | **需桥接（可统一）** | C# 已用 partial 家族逐个成员种类地扩（R2）；VB 13.3 走同一路径即对齐，不必造平行系统——正是本会议 RESOLUTION 3 的落点 |
| `MustReplace` 强制供体契约 | **兼容 + 增强** | C# 9 已把「显式访问性 partial 必须有定义」做成语言规则（R2 引文）；VB 13.3 partial 成员 + 强制定义 = C# 已验证过的模式 |
| 语法地基：同类型双同签名声明 | **冲突（语法层）** | C# 与 VB 规范的 partial 模型都是「一个逻辑成员 = 声明 + 定义，签名必须匹配」（C# 侧原文「Both declarations of a partial member must have matching signatures」，`partial-events-and-constructors.md`；VB 侧见 §Partial Methods「same signature」）。**「同一类型内两个同签名声明」在 C# 无对应、元数据无法表达**。effective-member 地基若按提案的「重复声明」模型建，会制造编译器单方语义，C# 消费者无法理解 |
| 元数据跨语言 | **需桥接** | `Replaces` 产物为普通方法，C# 消费者零摩擦；但「可替换/已替换」契约**不进元数据**，跨语言工具（C# 生成器想接管 VB 成员）无法识别——契约是源码级、按工具链的 |
| 与 AOT/trimming | **兼容** | 无运行时分派、无反射，比 `Any`/晚期绑定类提案安全得多（对照决策文件 M2/M5 的摩擦点） |
| 若把「替换」误读为继承层隐藏（new/hidden/overload 的替代） | **脱节（划界）** | 那是 R4 的 newslot/final 轴，属类型层次遮蔽；本提案是同一类型内编译期选择。混为一谈会把「零元数据特性」误建成「动元数据特性」 |

### 对 VBScript.NET 的适应建议

1. **13.3 定义为「VB 的 partial 全成员扩展」，与 C# 14 对齐**：C# 已把 partial 扩到事件/构造器；.vbx 的 13.3 应覆盖属性/事件/构造器级「双声明一供体」，让 .vbx 生成器生态能消费与 C# 生成器**相同**的模式，而不是另起一套。
2. **`MustReplace` 建在 partial 地基上、纯源码级契约**：不发射元数据，产物为普通方法；C# 消费者看到的就是一个普通方法，零摩擦。这是 .vbx 与 C# 生态互操作的正确形状。
3. **工具侧探测 + source-gen 桥**：默认「编译到受管程序集 + 生成器桥」，interpreted 模式做成显式 opt-in（决策文件 M5 同款）。`MustReplace` 契约在编译期闭合，天然 AOT/trimming 友好。
4. **跨语言识别新元数据/属性**：.vbx 编译器应能识别 C# partial 的「显式访问性 = 必须有定义」语义（消费 C# 生成器产物时）；对 `[OverloadResolutionPriority]`，建议**不**在 VB 绑定中 honor——保持 C#-only 优先序，避免两份绑定语义漂移。
5. **默认安全**：绝不让 `Replaceable` 变成运行时分派（那会同时撞 AOT 与 C# 静态模型）；保持「选中的成员就是普通方法」这一零运行期承诺，作为写入 spec 的硬约束。

### 对既有 RESOLUTION/三态判定的影响

- **三态表不受影响**；本附录为 RESOLUTION 1、3、4 提供 C# 侧的外部证据（R2/R3）。
- **新增一条实现注意**：effective-member 地基必须采用「declaration + definition」的 partial 模型（VB 规范 §Partial Methods 与 C# 9 扩展后的模型），而**不是**提案正文的「同类型双同签名声明」模型——后者在 C# 无对应、元数据无法表达，会重新制造「第二做事方式」（原则 #3）。这把 RESOLUTION 7 的符号表成本进一步压向 13.3，而非本提案自建。
- 任务提示的「new/hidden/overload 替代」角度：**与本提案无关，已划界**（R4）；若未来有独立提案做「VB 的 `new` 替代」，才需要评估 newslot/final 跨语言。

### 引用纪律与 OPEN QUESTIONS

**已核实的逐字引用**（来源均为 `..\..\csharplang`）：
- 「Given that the compiler doesn't allow generators to modify code hooking up this pattern would be pretty much impossible for generators.」→ `proposals\csharp-9.0\extending-partial-methods.md`
- 「The goal being to expand the set of scenarios in which these methods can work with source generators as well as being a more general declaration form for C# methods.」→ `proposals\csharp-9.0\extending-partial-methods.md`
- 「When a `partial` method has an explicit accessibility modifier the language will require that the declaration has a matching definition even when the accessibility is `private`」→ `proposals\csharp-9.0\extending-partial-methods.md`
- 「C# already supports partial methods, properties, and indexers. Partial events and constructors are missing.」→ `proposals\csharp-14.0\partial-events-and-constructors.md`
- 「Both declarations of a partial member must have matching signatures」→ `proposals\csharp-14.0\partial-events-and-constructors.md`
- 「We introduce a new attribute, `System.Runtime.CompilerServices.OverloadResolutionPriority`, that can be used by API authors to adjust the relative priority of overloads within a single type … not be chosen by C#'s overload resolution rules.」→ `proposals\csharp-13.0\overload-resolution-priority.md`
- 「All override declarations should omit the `newslot` bit to ensure no new vtable slot is allocated.」→ `meetings\2017\CLR-2017-03-23.md`

**OPEN QUESTIONS / Suspect**：
- `Suspect`：C# 是否有任何「生成器可修改/接管既有成员」的正式提案？本镜像未见（`extending-partial-methods.md` 反而明言生成器不可改既有代码）。倾向：无。
- `Suspect`：`new`→MethodAttributes.NewSlot、`sealed override`→Final 的精确位级映射属 ECMA-335（§II.22.28），本镜像 spec 目录只是指向 dotnet/csharpstandard 的链接索引，无法在本仓库逐字核实；上述表述为通用 CLR 元数据知识。
- `OPEN QUESTIONS`：C# 对「同一类型内两个同签名声明」是否可能有任何软化？本镜像未见任何提案方向。倾向：C# 永远不做（partial 模型已覆盖「声明/定义」需求）。
- `OPEN QUESTIONS`：.vbx 是否 honor `[OverloadResolutionPriority]`？建议不 honor（见适应建议 4），但需要一份 C#/VB 绑定分歧的兼容性文档。
