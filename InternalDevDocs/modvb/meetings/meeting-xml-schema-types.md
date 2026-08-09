# Visual Basic Language Design Meeting
August 8, 2026

ModVB 沙盒系列会议。本周回到我们自己的家——VB 的 XML 资产。这个话题在主线其实早就有一轮：2017-10-18 的 LDM 把 "XML types for XML IntelliSense" 放进过议程，当时对"更丰富的注释类型"的反馈偏向工具侧——"We might be able to do a lot with no compiler changes and should investigate with IDE team." 今天这份建议把同一条路往前推了一大步：从"注释 / 工具侧"直接推到"语言级类型"。我们花了整场追问：这一步值不值得迈。

## Agenda

* [Proposal: XML Schema 类型（XML Schema Types）](#proposal-xml-schema-类型)

## Proposal: XML Schema 类型

_Related: 主线会议纪要 2017-10-18（Annotated types / XML types for XML IntelliSense，`meetings/2017/vbldm-notes-2017.10.18.md`）；[vblang #139 – XML Patterns](https://github.com/dotnet/vblang/issues/139)；[vblang #184 – Tagged String literals](https://github.com/dotnet/vblang/issues/184)；[vblang #27 – GUID Literals](https://github.com/dotnet/vblang/issues/27)；[vblang #101 – JSON Literals](https://github.com/dotnet/vblang/issues/101)；ModVB：`annotated-types`（inactive）、`json-literals`、`json-pattern-matching`_

### 场景与缺口

VB 是唯一把 XML 做进语法的语言：XML 字面量、`.<name>` 子元素轴、`.@attr` 属性轴、`...<name>` 后代轴，全都是一等语法。We like this asset——它仍然是 VB 最独特的招牌之一，也是"几十万安静客户"里老一代业务开发者的真实记忆。但缺口从第一天就存在：**轴访问的成员是动态的**。今天的类型系统只告诉我们 `address.<Country>` 是 `IEnumerable(Of XNode)`（子元素轴映射到 `Elements(name)`，见规范 `expressions.md` §XML Member Access Expressions），它不告诉 IDE 这个元素下**允许**出现哪些子元素。于是没有 IntelliSense，也没有编译期校验——`address.<Country>` 和 `address.<Nonsense>` 在类型系统眼里完全等价。

而 VB 不是一直都没有 XML IntelliSense。按 Anthony 原文的追述，VS2008 时代的 VB 曾经有过基于 schema 文件的 XML IntelliSense，在构建 Roslyn 时因时间压力被砍掉，他一直在琢磨如何优雅地把它找回来（`Probably`：这是 Anthony 的自述，未在主线文档中独立验证）。今天的建议就是那个追忆的语言级化版本：让开发者声明"这个变量是 `<geo:Address>`"，编译器把 XSD 信息映射进类型系统，轴访问据此获得类型与补全。

```vb
' XML Schema "types" to re-enable XML IntelliSense.
Let address As <geo:Address> = ...
? address.<Country>.Value
```

值得先摆出来的是：这个缺口**主线和我们都认**。2017-10-18 主线会议在 "XML and JSON" 一节问的正是同一件事（逐字引用）：

> Is there an even richer notion of an annotated type which could be used to add and track simple information about XML and JSON literals to better enable the IDE to provide completion when using XML-axis properties or the dictionary-access operator?

并给出同样的雏形（逐字引用）：

```vb
Dim x As <contact>     ' Type: <contact>
Dim y = x.<address>    ' Type: <contact>.<address>
Dim z = y.<city>.Value
```

也就是说，`As <contact>` 这个语法形态和"轴访问逐步精化类型"的心智模型，主线在 2017 年就已经画出来了。今天的建议不是新方向，而是**把主线当年没往下走的那一步——语言级类型——走实**。所以本场的问题不是"要不要"，而是"以什么形态做"。

### 候选方案

**PROPOSAL A — 语言级 XML Schema 类型（建议原文）。** `Let address As <geo:Address>` 是一个真实类型标注，编译器读取 XSD，推导该 XML 允许的元素与属性，`.<Country>` 的绑定与结果类型据此改变，IntelliSense 列出 schema 允许的子元素。这是建议原文的形态——但它只给了两行示例，绑定模型、文法、转换规则一律未定义。

**PROPOSAL B — 注释型变体（annotation，与 `annotated-types` 方向一致）。** `<geo:Address>` 是对 `XElement` 变量的**注释**，不是新类型：运行时表示仍是 `XElement`，轴绑定零改变，编译器只把注释记录下来交给 IDE 做补全与诊断。先例是元组名——`(x As Integer, y As Integer)` 不生成新类型，只是把名字作为注释浮现在 `ValueTuple(Of Integer, Integer)` 上（Anthony 在 `annotated-types` 中主张）。这也正是 2017-10-18 主线偏好的落点："We might be able to do a lot with no compiler changes and should investigate with IDE team."

**PROPOSAL C — XSD → 强类型包装类（代码生成）。** 用 MSBuild/T4 把 XSD 转成包装类（`Address` 类、`Country As String` 属性……），轴访问变成对包装类的成员访问。这是 2017 会议所画光谱的"另一端"——"This is one end of the spectrum of providing a better tooling experience for untyped data over the wire. Type providers are on the other end." 现状的 `xsd.exe` / `dotnet-xscgen` 已在此存在。

**PROPOSAL D — 纯 IDE / 分析器（语言零改动）。** 保持无类型 XML，用分析器从变量初始化的 XML 结构、或从项目里附加的 schema 文件做启发式补全。即建议原文 Alternatives 的第 2 条，也即 2017 主线那句 "investigate with IDE team" 的字面意思。

### 权衡：Q&A

- **A vs B：`<geo:Address>` 到底该是"类型"还是"注释"？** 这是全场的轴心。B 的证据很硬：主线的元组名先例证明"注释"就足以驱动大量 IDE 价值；2017 主线已倾向"no compiler changes"。但 A 的辩护者指出，建议示例里 `address.<Country>.Value` 的 `.Value` 要"取回该元素的值"——若 `.<Country>` 结果仍是 `IEnumerable(Of XNode)`，`.Value` 今天就能工作（它是自动生成的聚合扩展属性），B 并不损失示例表达力。反过来，A 的"类型"承诺反而引入一串无解的问题：`<geo:Address>` 不是 CLR 类型，它运行时是什么？与 `XElement` 的转换、赋值、重载决议怎么走？这不是 B 的问题，是 A 自找的。**初步倾向 B。**
- **A vs C：为什么不干脆代码生成？** C 有真实的现状工具，但生成物是另一套类型体系，轴访问惯用法（`.<name>`、`.@attr`）在包装类上不成立，等于放弃 VB 的 XML 语法资产去换一个类库。A/B 的语法优势正是**延续**轴属性惯用法，而不是绕开它。C 被保留为"光谱另一端"的参照系，不作为候选。
- **A vs D：语言改动 vs 纯 IDE？** D 的问题是建议原文自己承认的：不引入新类型语法，就只能靠启发式，`address.<Country>` 依然可以拼错而不报错；且分析器要"检查每一个字符串/轴访问"才知道意图，这正是 2017 会议在 GUID 字面量讨论里否掉纯分析器路线时说的：分析器 "must inspect every string literal in the program" 且要猜意图。B 的注释语法正是为了解决"意图显式化"——用注释把"这变量是 Address"这句话写进代码，IDE 才知道拿哪份 schema。**结论：D 不是终点，是 B 的对照物。**
- **轴结果类型分叉（collection vs single）：schema 说 `Country` 最多一次，`.<Country>` 该不该从集合变成单值？** 这是 B 与 A 之间最危险的岔路。若 A 让 `.<Country>` 在 maxOccurs=1 时成为 `<geo:Country>` 单值，那么 `address.<Country>.Where(...)`、`address.<Country>.First()` 的语义随之改变——同样源码、重编译后行为不同。这正是设计原则所警惕的"隐蔽的控制流/语义变化"。**否决单值化**：轴结果维持集合语义，schema 只贡献补全与类型信息。
- **`geo:` 前缀怎么解析到 XSD？** 建议原文只说"应当可以解析到某个 XSD 定义"，没给机制。今天 VB 的 XML 命名空间前缀走 `Imports <xmlns:geo="http://example.org/geo">`，只负责字面量与轴访问的名字解析。若让 `geo:` 在前缀层面既管命名空间又管 schema 定位，是同一个前缀两种职责，我们不喜欢。**倾向独立机制（项目级 XSD 引用或内联声明），不重载 Imports。** 详见 OPEN QUESTIONS。
- **`.Value` 在注释模型下的语义。** 规范注明 `.Value` 是"如果扩展成员需要，就在产物程序集里自动定义"的扩展属性；对轴结果（集合）它聚合文本。B 不动它——`address.<Country>.Value` 继续聚合。A 若要按 schema 元素类型重新定义 `.Value`，又是第二处语义分叉。B 又一次胜出。

### 深度追问：LDM 拷问清单

#### 1. 语法 / 文法歧义

`As <geo:Address>` 在今天的 VB 里是**解析错误**——类型文法不含 `<`（VB 泛型用 `(Of T)`，不是 `<T>`）。因此新增语法是**加性的**，不破坏任何既有源码。真正的风险是**视觉与句法定位**：

- `As <` 之后到底是 schema 类型还是…… `<` 在表达式里是"小于"，在语句里是 XML 字面量开头（`Dim x = <contact/>` 合法）。`As` 位置把它们隔开了，但 `Function F() As <geo:Address>`、`Sub S(x As <geo:Address>)`、`List(Of <geo:Address>)` 每一处类型位置都要新增一条 `XMLSchemaTypeName ::= '<' XMLQualifiedName '>'` 的产生式，并且不能与既有类型文法（数组 `()`、可空 `?`、泛型 `(Of)`）组合出歧义。
- 与 `Imports <xmlns:...>` 共用 `<` 符号但没有重叠（那是语句级声明，这里是类型位置）。**可以解析，但必须写进文法，不能靠编辑器手滑。**

#### 2. 角案例 / 边界语义

- **轴结果分叉**（上文）：maxOccurs=1 单值化是最大的角案例陷阱，v1 否决。
- **可选元素与空集合**：`Country` 在 schema 里 `minOccurs="0"`。补全列出它，但运行时 `address.<Country>` 返回空集合，`.Value` 聚合空集为 `""`。注释模型下无歧义。
- **递归 schema**：

```vb
' <xs:element name="Person"><xs:complexType><xs:sequence>
'   <xs:element name="Name" type="xs:string"/>
'   <xs:element name="Child" type="Person" minOccurs="0"/>
' </xs:sequence></xs:complexType></xs:element>
Let person As <hr:Person> = ...
Let name = person.<Name>.Value          ' 注释模型：OK，补全来自 schema
Let child = person.<Child>.FirstOrDefault()  ' 仍是 IEnumerable(Of XNode)
```

递归作为**注释**完全可行（不建类型、不展开）；作为**类型**则要处理"无限类型"或图状结构——又一个 A 的负担。
- **`xs:any` 通配**：schema 允许任意扩展点时，静态信息到此为止，回落 `XElement`。A 的回落语义未定义（报错？警告？静默动态？）；B 只是"注释到此为止"，天然无歧义。
- **XML 字面量赋给注释变量**：

```vb
Let address As <geo:Address> = <geo:Address><geo:Country>US</geo:Country></geo:Address>
```

编译期做结构校验？运行期校验？v1 我们**不做校验**——注释不改变运行时行为；若要校验，交给分析器（Maybe）。
- **集合 / 泛型 / ByRef**：

```vb
Sub NormalizeAddress(ByRef address As <geo:Address>)   ' 可作参数类型？——注释模型下可以（底层是 XElement）
End Sub
Let addresses As List(Of <geo:Address>)               ' 泛型参数里是注释还是类型？OPEN QUESTION
```

#### 3. 作用域与绑定

注释模型下语义模型几乎不变：`address` 的类型仍是 `XElement`，`address.<Country>` 仍绑定 `Elements("Country")`，只是符号上多挂一个"schema 注释"供 IDE 与诊断消费。A 的类型模型则要求一个新的符号种类（schema 节点符号）与一套 `GetTypeInfo` 的返回值——Roslyn 语义模型、重载决议、表达式树全部要动。**这基本是 A 的死刑判决理由之一。**

#### 4. 与既有特性的交互

- **匿名类型名称推断**：规范 `expressions.md` 规定 `x.<y>.z` 推断名字 `z`、`x.<y>(0)` 推断名字 `y`。schema 注释不得干扰这些既有规则。
- **Option Strict Off / 晚期绑定**：宽松模式下 `address.<Nonsense>` 今天编译、运行期 `MissingMemberException`。注释不得把既有晚期绑定变成早期绑定报错——只做补全提示，不做绑定性诊断。
- **`!` 字典访问**：2017 会议把 XML 轴与 JSON 的 `!` 放在同一句话里讨论（"XML-axis properties or the dictionary-access operator"）。XML 侧没有 `!`，但 JSON 侧 `a!address` 要同样的注释机制——两套语法、一套机制。
- **LINQ to XML 互操作**：`address.<Country>` 流进查询（`From c In address.<Country> Where ...`）必须保持 `IEnumerable(Of XNode)`，否则查询推断全乱。

#### 5. Breaking change 与兼容性

- 加性语法（`As <...>` 今天不可解析）+ 注释零绑定变化 ⇒ **无破坏**。这是 B 相对 A 的压倒性优势。
- A 的两处破坏隐患（轴结果单值化、`.Value` 重定义）一旦做就是"同样源码重编译后行为变化"，与主线"几乎永不破坏"的立场直接冲突。文档未做任何兼容性分析——这是硬伤。
- 需要 `langversion` 门控与错误策略：注释语法在低版本下报"需要更高语言版本"。

#### 6. Option Strict / 编译选项分叉

Strict On：注释驱动补全，并可选择性地对"schema 明确不存在"的轴访问给出警告（警告，不是错误）。Strict Off：注释仅驱动补全，不改变任何绑定。两条路径对"注释是否存在"的可用成员集合保持一致。

#### 7. IDE / IntelliSense 影响

这是本特性的**核心价值面**，但建议原文对 IDE 只字未提。要问的：`address.` 与 `address.<` 之后补全什么（子元素名 / 属性名分开列？）；`address.@` 属性轴怎么提示；InfoTip 显示 `<geo:Address>` 还是 `XElement`；schema 缺失（前缀未解析）时的降级提示。2017 主线把 "investigate with IDE team" 留成了作业——我们也要做同样的作业，且要先回答"能否零编译器改动达成"。

#### 8. 数据 / 普遍性

最软的一环。2017 会议自己都承认 "JSON is the _lingua franca_ of the cloud."——XML 在数据交换里的生态位今天比 2008 年窄。`Suspect`：schema 优先（schema-first）的开发在今天更接近 niche 而非主流；多数 VB XML 用户写的是无 schema 的内部配置/文档 XML。"几十万安静客户"里需要 XSD 类型化的人数没有量化数据。这是 Table 的重要理由——价值真实但人群收缩。

#### 9. 更简替代

- 纯 IDE / 分析器（D）：建议原文自己的 Alternatives 第 2 条，2017 主线的字面倾向。局限是"意图要猜"——注释语法（B）把意图显式化，是它的增强。
- 代码生成包装类（C）：现状工具已存在，但不延续轴语法。
- 保持现状：不损失任何东西，只是继续没有 XML IntelliSense——"We're proud not to do anything" 的选项永远在桌上，而这次我们更想要注释语法。

#### 10. 复杂度 / 成本 / 优先级

A 的成本 = XSD 读取器进编译器 + 类型映射 + 绑定改变 + 语义模型改动 + IDE 协议，接近一个小语言特性；B 的成本 = 一条文法产生式 + 注释落到语义模型 + IDE 消费。优先级上：这不是头条特性，也排在模式匹配 / 可空性 / 交并类型之后；但它与 `annotated-types`（inactive）共享同一片实现面，**搭车成本远低于独立做**。值得做的是 B 的最小切面，不值得做 A。

#### 11. 运行时 / CLR 硬约束

注释模型：无 CLR 影响——运行时仍是 `XElement`，注释像元组名一样在编译产物里擦除或仅作元数据。A 的伪类型若真落地，至少不触 PEVerify，但"伪类型"的转换与重载决议会成为长期债务。表达式树按 `XElement` 生成，无碍。

#### 12. 值不值得做

价值：盘活 VB 最独特的资产（XML 语法 + IntelliSense），真实但人群收缩；成本：B 小、A 大；风险：B 近乎零、A 有两处隐蔽语义分叉。**结论：值得做的是注释（B），不值得做语言级类型（A）。** 若只做 A 而拒绝收敛到 B，我们会建议不做。

### VB 基因对照

- **不破坏现有代码（原则 #1）**：B 满分（加性语法 + 零绑定变化）；A 有轴结果单值化与 `.Value` 重定义两处隐患，扣分。
- **不引入"第二种做事方式"（原则 #3）**：这是本建议的最大扣分项。声明 XML 变量从此有两种写法（`As XElement` vs `As <geo:Address>`），schema 类型有注释与代码生成两条路，且与 `annotated-types` 的 `{"trade-message"}` 机制重复——除非收编成同一个"类型变体"机制，否则扩展表面失控。
- **避免隐蔽语义变化（原则 #7）**：A 的轴结果分叉正是主线当年拒 `Return?` 的同类理由；B 通过"零绑定变化"规避。
- **保持 VB-like（原则 #2）**：`<geo:Address>` 读起来像它所描述的 XML，延续轴属性惯用法——很 VB；但 `As <...>` 作为**类型**语法对 VB 是外来的（VB 用 `(Of)`），作为**注释**则门槛低得多。
- **不为边缘场景加特性（原则 #6）**：schema-first 开发是 niche，直接削弱本特性的普遍性论据。
- **与主线关系（对照表 2.3）**：主线 2017-10-18 已讨论同一方向并倾向"无编译器改动 + IDE 调查"，此后未再推进（`Probably`：XML types 此后在主线无记录在案的后续）；本建议把方向推成语言级类型 = **Anthony 独立延伸且更激进**，与 `null` 字面量、空安全全家桶同构（主线保守、Anthony 激进）。与 `annotated-types` 是同一片土壤，必须合并。

### RESOLUTION:

1. **语言级"XML Schema 类型"（PROPOSAL A）以现稿形态不进入设计管线**：两行示例、无文法、无绑定模型、轴结果分叉与 `.Value` 语义未决、无兼容性分析。**Table。**
2. **采纳方向 B（注释型变体）**：`<geo:Address>` 是对 `XElement` 变量的**注释**，不是新类型；运行时表示与轴绑定零改变。先例：元组名对 `ValueTuple` 的注释（Anthony 主张，2017 主线注释类型讨论同向）。
3. **语法保留 `<ns:Name>` 形式**，与 2017-10-18 主线样例 `Dim x As <contact>` 一致；`As <...>` 今日为解析错误，属加性语法，不破坏既有代码。
4. **轴结果维持既有集合语义**（`Elements(name)` → `IEnumerable(Of XNode)`）；注释只驱动 IDE 补全与诊断，不改绑定。与 2017 主线 "We might be able to do a lot with no compiler changes and should investigate with IDE team." 对齐。`Probably`：这已能恢复 XML IntelliSense 的主要价值。
5. **否决轴结果单值化与 `.Value` 重定义**——隐蔽语义变化，违反原则 #7。
6. **`geo:` 前缀解析不重载 `Imports <xmlns:...>`**；项目级 XSD 引用或内联声明留待设计（OPEN QUESTION）。
7. **并入 `annotated-types` 的"类型变体/注释"统一机制**，JSON 侧 `{"trade-message"}` 同机制；本建议作为该机制的第一个用例。单独成案无意义。
8. **Option Strict 分叉**：注释仅在 Strict On 下可选择性地产生"schema 明确不允许"的**警告**（非错误）；Strict Off 保持今天行为。两路径补全一致。
9. **XML Patterns（#139）一并 Table**——主线 2017-10-18 已对同族的 JSON patterns 记录 "Decision: Table, wait for feedback/scenarios and more matching"；在注释类型落地前没有模式匹配的地基。

### Implication:

- 与 `annotated-types` 工作项合并，起草"类型变体/注释"speclet：语法、语义模型表示、IDE 协议、与元组名先例的一致性。
- 完成 2017 主线遗留的作业：**调查纯 IDE / 分析器可否零编译器改动恢复 schema 驱动的 XML IntelliSense**；若可行，B 退化为"仅为该机制加一条语法"。
- 写最小原型（若走编译器改动）：解析 `As <ns:Name>` → 注释挂到 `XElement` 局部变量 → 语义模型暴露 → IDE 补全 `address.<`。
- 起草 Compatibility 分析：轴绑定、`.Value`、匿名类型名推断、Option Strict 分叉、`langversion` 门控。
- 未决问题移交至 OPEN QUESTIONS。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`：`geo:` 前缀 → XSD 的解析机制（项目级引用 / 内联 / 不解决则退化）。
- `OPEN QUESTIONS`：注释在泛型（`List(Of <geo:Address>)`）、集合、ByRef、函数返回类型中的存活与传播规则。
- `OPEN QUESTIONS`：`xs:any` / 递归 / 可选元素在注释中的表示（递归可行；any 回落动态；可选仅影响补全）。
- `OPEN QUESTIONS`：XML 字面量赋给注释变量时是否做结构校验——v1 明确**不做**；分析器路线（Maybe）待评估。
- `TODO`：量化 schema 优先开发的真实占比，为数据/普遍性补证据。
- `TODO`：核查 VS2008 时代 XML IntelliSense 的实现细节（Anthony 自述被砍于 Roslyn 构建期；`Suspect` 待独立验证）。
- `Follow-up`：与 `json-literals` / `json-pattern-matching` 对表——JSON 侧若先落地注释语法，XML 侧必须复用同一机制。

### 状态

- **LDM 状态**：语言级类型 → **LDM Considering（暂缓）**；注释型变体方向 → **LDM In Process**（并入 `annotated-types`）。
- **三态判定：Table（建议原样）**；方向 B 为 **Consider**——值得做的是注释，不是语言级类型。

---

## 附录：特性评价

# 建议评价报告：proposal-xml-schema-types.md

## 评价对象

- 建议：proposal-xml-schema-types.md — XML Schema 类型（`<geo:Address>` 语言级类型标注，重启用 XML IntelliSense）
- 来源：Anthony 原文第 4 章（"XML Schema types to re-enable XML IntelliSense"）；语法形态与主线 2017-10-18 "XML types for XML IntelliSense" 讨论一致；底层动机（VS2008 schema IntelliSense 被砍）承自 Anthony 自述，与 `annotated-types`（inactive）同源
- 配方目标：把 XSD 信息映射为类型系统的一部分，让 `address.<Country>.Value` 恢复静态检查与 IntelliSense

## 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在的问题 |
|------|------|------|----------|------|
| 效果 | 3/5 | 锚点 3："只覆盖部分场景；主效果显现但关键子效果缺失/消退"。Motivation 清晰（恢复 XML IntelliSense）、示例可操作（`? address.<Country>.Value`），但全文仅两行设计；赋值、集合、泛型、ByRef、schema 特性回落等关键子效果全部缺失 | 已检查 | 无原型封顶；轴结果分叉未定 = 主效果（静态类型）以何种形态显现都不清楚 |
| 特性 | 3/5 | 锚点 3："明显借鉴外部但做了 VB 化改造；或打包了次要无关能力"。延续 VB 自己的 XML 资产（轴属性、字面量）是强基因；但 `As <...>` 类型语法对 VB 类型文法（`(Of)`）是外来形态，且与 `annotated-types` 的 `{"trade-message"}` 机制重复（"第二种做事方式"风险），类型模型隐含隐蔽语义变化（原则 #7） | 已检查 | 语言级"类型"形态与 VB 基因冲突；注释形态才能满格 |
| 品质 | 3/5 | 锚点 3："缺某一章节或在关键处边界含糊；未决问题被轻描淡写"。六章节齐全、Drawbacks/Alternatives 诚实、未决问题具体（4 个关键点，如实列出是加分）；但 Detailed design 近乎空壳（无文法、无绑定模型、无转换、无 schema 特性处理），状态行是占位链接 | 已检查 | 缺 Compatibility/breaking-change 章节；无文法；未引用 2017-10-18 主线讨论（同一方向的既有工作） |
| 属性 | 3/5 | 锚点 3："有得有失——某维度受益、某维度受损，文档未充分权衡"。光/水正向（盘活 VB 独有的 XML 资产、差异化于 C#）；风/暗受损（与主线"无编译器改动 + IDE 调查"的倾向断裂、schema 前缀重载 Imports、轴结果分叉），文档未权衡 | 已检查（预测待定） | 与主线关系未讨论；两处隐蔽语义分叉未识别；实际影响须"已采纳"后定 |
| 炼金成分 | 3/5 | 锚点 3："部分来源未标注（借鉴了 C# 却没提）；标注与影响有偏差"。材料=Anthony 第 4 章（已标注）；但①未标注与主线 2017-10-18 同一方向的直接关系；②未标注承自 VS2008 时代资产（仅 `annotated-types` 提及）；③未标注与 `annotated-types` 机制重复；④"类型"成分实际影响被高估——它带来的绑定/校验承诺在文档层面无法兑现 | 已检查 | 来源标注不完整；类型 vs 注释的成分选择未论证，影响预估偏乐观 |

## 设计原则对照

- **与 VB 基因：部分一致**——延续 XML 轴属性惯用法（#2、#5）、零破坏的注释形态符合（#1）；但"语言级类型"形态引入陌生的 `As <...>` 类型语法（#2）、轴结果单值化违反隐蔽语义变化禁令（#7）、与既有/兄弟机制重复（#3）、schema-first 是边缘场景（#6）。
- **与主线关系：Anthony 独立延伸（更激进）**——主线 2017-10-18 讨论了同一方向并倾向"无编译器改动 + IDE 调查"（"We might be able to do a lot with no compiler changes and should investigate with IDE team."），此后无在案后续（`Probably`）；本建议把方向推成语言级类型，与 `null` 字面量、空安全全家桶同构（主线保守，Anthony 激进）。与 `annotated-types` 同源，必须合并；JSON 侧姊妹建议共享同一注释机制。
- **破坏性变更：现稿无（加性语法）；类型形态潜在有**——轴结果单值化、`.Value` 重定义会改变既有轴访问语义；文档未做任何兼容性分析。

## 总评

- **达成程度：部分达成**——动机与价值成立、方向有主线血缘；设计与安全性论证未完成，且"语言级类型"形态超出其证据支撑。
- **LDM 三态建议：Table**——语言级类型按现稿不进入设计管线；注释型变体方向（B）为 **Consider**，并入 `annotated-types` 后可作为"类型变体"机制的第一个用例推进。
- **主要问题**：① Detailed design 只有两行，无文法/绑定模型/schema 特性处理；② 轴结果类型分叉（集合 vs 单值）未识别，是类型形态的隐蔽破坏源；③ `geo:` 前缀解析未定义，且会重载 `Imports <xmlns:...>`；④ 与 `annotated-types` 机制重复，独立成案无意义；⑤ 未引用 2017-10-18 主线同一方向的讨论与 #139/#184/#101；⑥ 状态行占位链接；⑦ schema-first 开发是 niche，普遍性数据缺失。

## 返工建议

- **补充章节**：Detailed design（文法 `XMLSchemaTypeName ::= '<' XMLQualifiedName '>'`、注释 vs 类型的选择论证、schema 特性——可选/递归/`xs:any`——的处理与回落、转换与赋值规则）；Compatibility/breaking-change（轴绑定、`.Value`、匿名类型名推断、Option Strict 分叉、`langversion` 门控）。
- **补充证据**：引用并回应 2017-10-18 主线讨论（同方向、IDE-first 倾向）；完成"纯 IDE/分析器可否零编译器改动"的调查；最小原型（解析 → 语义模型 → IDE 补全）；XSD 驱动开发占比数据；VS2008 时代实现细节的独立核查。
- **未决问题处理**：明确"注释而非类型"的立场（先例：元组名）；轴结果保持集合语义；`geo:` 前缀走独立机制（项目级 XSD 引用），不重载 Imports；v1 不做结构校验（分析器 Maybe）。
- **设计探索**：与 `annotated-types` 合并为统一的"类型变体/注释"机制（XML 侧 `<geo:Address>`、JSON 侧 `{"trade-message"}`、匿名委托 `<Function(...) As ...>`、度量单位）；与 `json-literals` / `json-pattern-matching` 对表；与元组名注释、`Any` 伪类型（`proposal-any-pseudotype.md`）的表示一致性。

---

## 附录：C# 生态与互操作考量

### 关系强度声明

本提案（XML Schema 类型）是 XML 生态特性，**与 C# interop 的直接对应关系弱**：C# 语言层没有 XML schema 类型，`csharplang` 的 `proposals` 目录下**没有任何以 XSD / XML Schema / schema-first 为主题的提案文件**（Grep 文件级核实，仅 `target-typed-new.md` 与 `top-level-members.md` 有旁及的 "IntelliSense" 一词）。正文第 11 节「运行时 / CLR 硬约束」也确认：注释模型无 CLR 影响，运行时仍是 `XElement`。

但有三条 C# 现实方向确实投影到这个提案上，其中一条还是本场 RESOLUTION 方向（PROPOSAL B 注释型变体）**最强的外部先例**：C# 8 可空引用类型本身就是"在不变运行时类型之上叠加元数据注释"的语言级实践。另有 C# 对格式特定字面量/XML 字面量的两次明确拒绝，直接关系到本场对 PROPOSAL A（语言级类型）的 Table 判定。附录按这些轴对照，不硬凑。

### 相关 C# 现实方向

#### D1 C# 8 可空引用类型 = "注释即元数据、运行时类型不变"（PROPOSAL B 的最强先例）

C# 8 的可空引用类型与本提案的 PROPOSAL B 是**同一模型**：`string?` 的运行时类型仍是 `string`，可空性只是叠加的注释，由编译器流分析与 IDE 消费。其「Metadata representation」一节原文：

> "Nullability adornments should be represented in metadata as attributes. This means that downlevel compilers will ignore them."
> → `proposals\csharp-8.0\nullable-reference-types.md`（Metadata representation）

把这句话里的 "Nullability" 换成 "schema"，就是本场 RESOLUTION 第 2/4 条的 C# 侧表述：`<geo:Address>` 是对 `XElement` 变量的注释，运行时表示不变，注释进元数据供 IDE/诊断消费，additive。且 C# 对"注释改变既有代码"的警惕与本场 Option Strict 分叉同构：

> "I want the bug fixes, but I am not ready to deal with their new annotations"
> → `proposals\csharp-8.0\nullable-reference-types.md`

这印证了本场 RESOLUTION 第 8 条（注释仅在 Strict On 下可选地产生警告、Strict Off 保持今天行为）——C# 侧先例同样是"注释 opt-in、可忽略、不改变既有语义"。

#### D2 C# 对格式特定字面量 / XML 字面量的明确拒绝（关系到 A 的 Table 判定）

C# 2015 年在 triage 中把 XML literals 直接放进 "Never"，原文逐字只有一句：

> "Never! We won't bake in a specific format."
> → `meetings\2015\LDM-2015-05-25.md`（# XML literals，issue #1746 → Never）

同一时期的记录讨论"语言特性该不该内建序列化/数据绑定技术"时，给出了同样取向的概括：

> "There will not be built-in JSON literals, or `INotifyPropertyChanged` implementation or anything like that."
> → `meetings\2015\LDM-2015-03-04.md`

注意这里的轴：C# 拒绝的是**把数据格式烤进语言语法**（新字面量形态）。VB 的情况不同——XML 字面量是 VB 9 起就存在的既有资产，本提案不新增"格式语法"，而是在既有 XML 资产之上叠加**类型/注释信息**。因此 C# 的拒绝不否定本提案的场景，但它确实指向一个分界：**语言级 XML schema 类型（PROPOSAL A）** 正是把一种数据格式（XSD）烤进类型系统，走的是 C# 明确拒绝的方向；**注释（PROPOSAL B）** 走的是 C# 8 实际采纳的方向（D1）。

#### D3 类型化数据的生态答案：source-gen + 运行时序列化器 + 代码生成（AOT 压力）

C#/.NET 生态对"wire 上无类型数据的类型化"给出的答案不是语言级 schema 类型，而是三条互补路径，全部落在 dotnet/runtime 与既有工具，不在 csharplang 语言面：

- **运行时序列化器**：`System.Text.Json`（JSON 侧）与 `System.Xml.Serialization.XmlSerializer`（XML 侧）承担"数据 ↔ 强类型对象"；XmlSerializer 在 .NET 亦有 source-gen 变体。
- **代码生成**：`xsd.exe` / `dotnet-xscgen` 把 XSD 转成强类型类——正是本提案 PROPOSAL C 所指的"光谱另一端"，且已存在于现状工具。
- **AOT/trimming 驱动的编译期生成**：C# LDM 讨论 interceptors 时，明确把"反射型场景难以 AOT 兼容"作为类型系统外信息的通病：

> "This approach is necessitated for all the reflection-based scenarios that use information that exists outside the type system to affect runtime code; because these scenarios use information not statically available during compilation, it is hard to make them AOT-compatible."
> → `meetings\2023\LDM-2023-07-24.md`（Interceptors）

映射到本提案：PROPOSAL C（代码生成）与 PROPOSAL D（纯 IDE/分析器）正是生态共识在 VB 侧的投影——**类型化 XML 主要靠工具链与生成器，而非语言级类型**。这与索引 T5/T6（类型系统与 source-gen 承担更多职责以服务 NativeAOT/trimming）、M5/M6 的结论一致。

#### D4 嵌入式字面量哲学对照：C# 12 collection expressions（嵌入式字面量，仅限框架中立数据）

C# 12 集合表达式是"嵌入式字面量"哲学在 C# 侧的落点，其 Summary 原句：

> "Collection expressions introduce a new terse syntax, `[e1, e2, e3, etc]`, to create common collection values."
> → `proposals\csharp-12.0\collection-expressions.md`（Summary）

Motivation 给出"字面量让编译器有优化自由度"的理由，与 VB 侧 2017-10-18 的"构造仪式遮蔽结构"判据同一直觉：

> "Having a literal form allows for maximum flexibility from the compiler implementation to optimize the literal to produce at least as good a result as a user could provide, but with simple code."
> → `proposals\csharp-12.0\collection-expressions.md`（Motivation）

但注意边界：C# 只对**框架中立的数据容器**（数组、`Span<T>`、`List<T>` 等）做字面量，从没把字面量/类型化扩展到 XML 或 schema。本提案的轴不是"构造"而是"类型化/注释"——与 collection expressions 共享"数据不该只是字符串"的直觉，但落在构造之外的另一端。这一轴 C# 明确留给了运行时序列化器与代码生成（D3），正如 D2 所示。

### 现实 vs 提案

| C# 现实方向 | 本提案对应面 | 判定 | 理由 |
|---|---|---|---|
| C# 8 可空引用类型 = 元数据注释、运行时类型不变、downlevel 编译器忽略（D1） | PROPOSAL B（注释型变体） | **兼容 / 强化** | 同一模型：`string?` 仍是 `string`，`<geo:Address>` 仍是 `XElement`；C# 证明"注释 + 元数据 + opt-in 警告"足以交付编译器/IDE 静态价值，无需新 CLR 类型 |
| C# 明确拒绝格式特定字面量（XML literals → "Never! We won't bake in a specific format."；"There will not be built-in JSON literals..."）（D2） | PROPOSAL A（语言级 XML Schema 类型） | **冲突（对 A）/ 脱节（对 B）** | C# 拒绝的是"把数据格式烤进语言"；VB 已有 XML 字面量资产，本提案是"类型化既有资产"而非"新增格式语法"，与 C# 的拒绝不同轴。但 A 的语言级 schema 类型恰是 C# 明确不走的"把 schema 烤进类型系统"方向；B 的注释是 C# 8 实际走的方向 |
| 类型化数据的生态答案 = 运行时序列化器 + 代码生成 + source-gen（AOT 压力）（D3） | PROPOSAL C（XSD → 代码生成包装类）+ D（纯 IDE/分析器） | **兼容 / 需桥接** | 生态把"XML/JSON 类型化"交给 `XmlSerializer`/`System.Text.Json`、`xsd.exe`/`dotnet-xscgen` 与生成器，而非语言级类型；VBScript.NET 复用即可，无需在语言面造序列化器 |
| C# 12 collection expressions（嵌入式字面量哲学，仅框架中立数据）（D4） | 本提案的"数据可见性"直觉 | **兼容（哲学层面）** | 共享"数据不该只是字符串、该让编译器/IDE 看见"的直觉；但 C# 只对框架中立集合做字面量，不做 XML/schema 类型化——两轴互补不重叠 |
| CLR / 元数据 / 互操作主线（索引 T2/T3/T4：Span、指针、COM、marshaling） | 本提案无 CLR 约束 | **脱节（如实）** | 无指针、无 marshaling、无新调用约定、无新元数据需求；interop 索引 T2/T3/T4 主线均不适用。唯一的互操作面是"注释的元数据表示"（见适应建议） |

### 对 VBScript.NET 的适应建议

- **注释的元数据表示以 C# 8 可空注释为蓝本。** PROPOSAL B 的注释若落元数据，直接采用 C# 8 的模型：运行时类型不变（`XElement`）、注释为属性、downlevel 编译器忽略。这让 VBScript.NET 的 schema 注释与 C# 的可空注释共享同一种"注释不改运行时"的元数据哲学，互读互写无需新护栏；若走元组名式擦除，则退化为纯 IDE 数据。
- **默认安全 / 按需动态。** 本场 RESOLUTION 已定：注释零绑定变化（默认安全），Strict Off 保持今天行为（按需动态）。C# 可空注释的 opt-in 警告模型（"I want the bug fixes, but I am not ready to deal with their new annotations"）正是 Option Strict 分叉的 C# 侧先例——注释必须可忽略、可逐步采纳。
- **source-gen 桥。** VBScript.NET 若要做"强类型 XML"，应复用现状工具（`xsd.exe` / `dotnet-xscgen` / XmlSerializer source-gen），把 PROPOSAL C 作为"强类型出口"、把 B 的注释作为"轻量语言面"。两层共存对应"脚本层允许动态、编译产物走类型化"的双模路线（决策文件 M5/M8）——这与 C# 生态"类型化 XML 靠工具链"的克制一致。
- **识别既有注释元数据。** Roslyn VB 编译器消费 C# 程序集时已须识别 C# 8 可空注释元数据（`NullableAttribute`/`NullableContextAttribute`）；若 schema 注释落元数据，须与可空注释互不干扰——两者都是"注释不改运行时"的属性，VBScript.NET 应按同一模型识别与发出（与决策文件 M8"必须认识 RequiresUnsafeAttribute 等新元数据"同理）。

### 对既有 RESOLUTION / 三态判定的影响

**判定不变，且被 C# 现实从生态侧加固。** 直接对应关系虽弱，但相关的 C# 证据没有一条推翻本场结论，反而逐一指向同向：

1. **PROPOSAL B（Consider）被 C# 8 可空先例直接支持。** C# 8 是"在不变运行时类型上叠加元数据注释、由编译器流分析与 IDE 消费、opt-in 警告"的生产级实践——本场 RESOLUTION 第 2/4 条（注释而非类型、零绑定变化）与 C# 侧最强的相关实践同构。这比元组名先例更近：元组名是"名称注释"，可空是"类型信息注释"，schema 注释落在同一谱系。
2. **PROPOSAL A（Table）被 C# 的格式特定字面量拒绝立场加固。** C# 2015 年对 XML literals 的裁定是 "Never! We won't bake in a specific format."——语言级 XML schema 类型正是把一种数据格式（XSD）烤进类型系统，与生态方向相反。C# 拒绝的是"新字面量语法"这一轴，但"把格式烤进语言"的精神与本场对 A 的否决（成本大、隐蔽语义分叉、无兼容性分析）一致。
3. **本场最软的"数据/普遍性"环节（第 8 节，schema-first 是 niche）被 C# 现实印证。** `csharplang` 无任何 XML Schema/XSD 提案（文件级核实），C# 生态从未把 schema 类型化当作语言需求——schema 类型化停留在工具链（xsd 代码生成）与序列化器层面，正对应"人群收缩、value 真实"的判断。
4. **补充一条证据建议。** 把 C# 8 可空注释的元数据模型写进 B 的 speclet（注释怎么落元数据、怎么被 IDE 消费、怎么被 downlevel 忽略），比只引元组名先例更有说服力，也为 annotated-types 的"类型变体/注释"统一机制提供 C# 侧的锚点。

### 引用清单（本附录逐字引用的 C# 原文）

| C# 原文（逐字） | 来源文件 |
|---|---|
| "Nullability adornments should be represented in metadata as attributes. This means that downlevel compilers will ignore them." | `proposals\csharp-8.0\nullable-reference-types.md`（Metadata representation） |
| "I want the bug fixes, but I am not ready to deal with their new annotations" | `proposals\csharp-8.0\nullable-reference-types.md` |
| "Never! We won't bake in a specific format." | `meetings\2015\LDM-2015-05-25.md`（# XML literals，issue #1746 → Never） |
| "There will not be built-in JSON literals, or `INotifyPropertyChanged` implementation or anything like that." | `meetings\2015\LDM-2015-03-04.md` |
| "This approach is necessitated for all the reflection-based scenarios that use information that exists outside the type system to affect runtime code; because these scenarios use information not statically available during compilation, it is hard to make them AOT-compatible." | `meetings\2023\LDM-2023-07-24.md`（Interceptors） |
| "Collection expressions introduce a new terse syntax, `[e1, e2, e3, etc]`, to create common collection values." | `proposals\csharp-12.0\collection-expressions.md`（Summary） |
| "Having a literal form allows for maximum flexibility from the compiler implementation to optimize the literal to produce at least as good a result as a user could provide, but with simple code." | `proposals\csharp-12.0\collection-expressions.md`（Motivation） |

### OPEN QUESTIONS

- **无 C# 原文存疑点**：本附录所有逐字引用均已直接在 `..\..\csharplang` 对应文件核实，无 **Suspect** 项。
- **开放问题（非本提案范围）**：`XmlSerializer` / `System.Text.Json` source-gen 对 XML 的精确支持状态属 dotnet/runtime 生态，`csharplang` 无正文可核实（与索引第四节 OPEN QUESTIONS 关于 COM source generator 属 dotnet/runtime 一致）。
- **开放问题（本提案范围，待 annotated-types speclet）**：schema 注释的元数据表示未定——属性（C# 8 可空模型）、元组名式擦除、还是纯 IDE 数据；这是 VBScript.NET 与 C# 互操作面的唯一实质接口，须在"类型变体/注释"统一机制里与 C# 侧表示对齐。
