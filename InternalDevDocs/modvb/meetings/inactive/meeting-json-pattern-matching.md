# Visual Basic Language Design Meeting
August 8, 2026

## Agenda
* [ModVB Proposal — JSON Pattern Matching](#modvb-proposal--json-pattern-matching)

## ModVB Proposal — JSON Pattern Matching

We are reviewing the ModVB proposal family for VBScript.NET. Today we took up JSON pattern matching, which proposes two things bound together: a **JSON shape pattern** (`ShapeOf expr Is { ... }`) that asserts or dispatches on the field shape of a payload, and a **JSON Schema type** (`{"http://.../employee"}`) that annotates a value with a schema URI so that `employee!emergencyContact` gets IntelliSense and compile-time analysis.

This is not a fresh conversation. In mainline vblang we already talked about both halves: **2017.10.18** took up JSON literals and "JSON Pattern Matching" as part of the same meeting, and tabled the pattern part with "wait for feedback/scenarios and more matching"; the same meeting discussed `{"contact"}`-style annotated types for JSON IntelliSense under "XML and JSON". We are therefore reviewing a resurrection, and we must say plainly whether anything has changed since 2017. `Probably` the honest answer is: the scenario is stronger for VBScript.NET than it was for stock VB, but the *design* is not more advanced than the mainline discussion it does not cite.

_Note on honesty: this note reviews `proposal-json-pattern-matching.md`（来源：Anthony D. Green 原文第 5 章 "JSON and JSON Pattern Matching"，另涉 18.7 反序列化守卫与 3.12 postfix cast 组合）. Statements are layered as 事实 / `Probably` / `Suspect` / `OPEN QUESTIONS` / `TODO`. All mainline quotes were re-verified against `..\..\vblang` before writing; anything we could not verify is marked `Suspect`. We record rationale so we can return later and see why we did things the way we did._

---

### 场景与缺口

The scenario is real and it is the strongest part of the proposal. JSON is "the _lingua franca_ of the cloud", and "first-class JSON support could be a strong attractant for first-time developers"（事实，2017.10.18）。For a VBScript.NET target this is doubly true: VBScript's world is heterogeneous payloads — HTTP responses, config blocks, `account!contacts`-style hierarchical dictionaries — where the *structure* of the data is the thing being inspected. Today that inspection is manual per field:

```vb
' Status quo：断言服务返回值形状，逐字段手工比对。
Dim newEmployee As Employee = Await service.AddEmployeeAsync("Jack", "Sparrow")

Assert.IsTrue(newEmployee IsNot Nothing)
Assert.AreEqual("Jack", newEmployee.FirstName)              ' 三个字段，三次出错机会。
Assert.AreEqual("Sparrow", newEmployee.LastName)
Assert.AreEqual("jack.sparrow@company.com", newEmployee.EmailAddress)
```

The proposal's counter is one shape test（Anthony 第 5 章原文逐字）:

```vb
' 建议原文：一个形状测试代替三个断言。
Assert.IsTrue(ShapeOf newEmployee Is {
                "firstName": "Jack",
                "lastName": "Sparrow",
                "emailAddress": "jack.sparrow@company.com"
              })
```

And the *dispatch* scenario — the one mainline actually cared about — is dispatching over multiple possible structures nested within a larger one. Mainline's motivating example from **2017.10.18** is still the best statement of the gap, and we quote it verbatim:

```vb
' Mainline 2017.10.18 原文：在异构集合内按形状分发。
For Each contact In account!contacts
    Select Case contact
        Case Match { "type": "business",
                     "company_name": company,
                     "address": address }

            ' Handling for businesses.

        Case Match { "type": "individual",
                     "email": email }

            ' Handling for individuals.
    End Select
Next
```

The principle mainline articulated that day fits the ShapeOf family exactly: a specialized pattern is worth it "when the ceremony of _inspecting_ an object(-graph) obscures the structure of the data"（事实，2017.10.18）。By that principle, JSON shape matching passes. We should say so.

But the gap statement in *this* proposal is thinner than the 2017 example. The proposal only shows the *assertion* form (`Assert.IsTrue(...)`), which is a Boolean probe; it never shows the *binding* form — the thing that makes patterns patterns. Its own schema half is a different feature (IDE annotation), and it is unclear whether the two halves share any mechanism at all. We went in liking the scenario and came out liking only part of the syntax, and we are `Suspect` that the proposal bundles two features that would otherwise be evaluated separately.

---

### 候选方案

We considered four shapes the feature could take.

**PROPOSAL A — 建议原文（Anthony 第 5 章）：JSON 形状模式 + JSON Schema 类型捆绑**

The proposal as written. The shape pattern uses the `ShapeOf ... Is { ... }` operator form; the schema half introduces `{"http://.../employee"}` as a type annotation and `employee!emergencyContact` as schema-driven member access（Anthony 第 5 章原文逐字）:

```vb
' JSON schema types for IntelliSense/analyzers.
Let employee As {"http://.../employee"} = ...
? employee!emergencyContact
```

The two halves share only the `{ ... }` token. The proposal's own `Drawbacks` concede the pattern is weak ("无法表达任意类型、可选字段"), the schema mechanism tensions with offline compilation, and `!` needs distinguishing from dictionary access — three concessions that together tell us the proposal knows its own boundaries are unresolved.

**PROPOSAL B — 拆开：JSON 形状模式进家族文法，Schema 类型进 annotated-types 线程**

JSON shape matching becomes one *literal/structural pattern* of the ShapeOf family grammar, using the family's chosen carriers rather than a new operator: `Case { ... }` inside `Select Case`, and the mainline `Matches` keyword for expression/assertion contexts. No new `ShapeOf` operator, no `Is` overload. The schema-annotation half is split out and merged into the inactive `proposal-annotated-types.md` thread, where `{"trade-message"}`-style annotations already live（事实，`..\proposals\inactive\proposal-annotated-types.md`）。`!` keeps its existing dictionary-access semantics; the schema only informs the IDE.

**PROPOSAL C — 只做 IDE：不加编译器语义，把 JSON schema 交给分析器/IDE 团队**

Mainline's own lean at **2017.10.18**: after discussing `{"contact"}` annotations it recorded "We might be able to do a lot with no compiler changes and should investigate with IDE team"（事实）。Under C, JSON shape matching is deferred entirely; the schema annotation is a metadata/decoration contract (like a comment, "absolutely no effect on compilation") that analysis and IDE features consume. This is the lowest-cost honest option for the schema half.

**PROPOSAL D — 什么都不做**

Keep `Assert.AreEqual` chains and manual `JObject` access; let `JsonSerializer` + strong types cover the typed cases. We noted that `JsonSerializer.Deserialize(Of Employee)` genuinely *eliminates* the assertion scenario for typed payloads — but it does nothing for the heterogeneous `account!contacts` dispatch case, which is where the value concentrates. So "nothing" is defensible only if we judge the heterogeneous case too rare; mainline tabled the feature precisely because "feedback/scenarios and more matching" were missing（事实，2017.10.18）。

---

### 权衡（LDM 追问清单）

We worked through the twelve-question checklist. The decisive questions were 1, 4, 5, 6 and 9.

**Q1. 语法/文法歧义：`{ ... }` 到底有几种读法？**

This is the first wall. `{ ... }` already carries a heavy load in VB: array literals (`{1,2,3}`), collection initializers, anonymous-type initializers (`New With { .name = "fred" }`). The ModVB family then proposes at least four more meanings: JSON literal for target-typed creation and `&=` writer chunks（`proposal-json-literals.md`）、JSON shape pattern（本建议）、and `{"uri"}` schema type annotation. The same token, five meanings.

Inside `ShapeOf newEmployee Is { ... }`, the ambiguity is acute because `Is` already means reference equality. Is `{ ... }` a *pattern* (shape match) or a *JSON literal value* (reference equality against a freshly built JSON object)? This is exactly the ambiguity mainline flagged for `Is` in **2018.12.19**: "We think `Is` will have ambiguity issues with the existing use for reference equality." The proposal does not address it. `Suspect`: the colon inside `{ "firstName": "Jack" }` saves us from the array-literal reading (`:` is not an array element separator today), but it does nothing to separate "pattern" from "JSON literal value" on the right of `Is`.

There is also no precedent for `{ ... }` as a *type*. `Dim employee As {"http://.../employee"}` reads as a one-element set containing a string — not a JSON object, not a type name. Mainline toyed with `Dim a As {"contact"}` in **2017.10.18** but never settled the syntax; the inactive annotated-types proposal calls the whole family "实验性 / 未定稿"（事实）。A type syntax with no decision behind it is a weak foundation for a resurrection.

**Q2. 角案例/边界语义**

The matching semantics are almost entirely unspecified, and this is where a real LDM would spend a day. We enumerate what must be answered before this is a language feature:

- **大小写与属性映射**：if `newEmployee` is a statically-typed `Employee` (PascalCase `FirstName`), how does `"firstName"` map to it? Case-insensitive? Naming convention? If it is a `JsonObject`/dictionary, it is key matching. The two paths are different features with different costs — `OPEN QUESTION`.
- **子集匹配 vs 精确匹配**：does the pattern require all fields present in the object, or only that every listed field matches (extra fields allowed)? JSON Schema semantics suggest subset; the assertion example implies exact. Undefined — `OPEN QUESTION`.
- **字段顺序无关**：JSON objects are unordered; `{ "lastName": ..., "firstName": ... }` must match the same shape as the example. `Probably` easy, but must be specified.
- **值比较语义**：`"firstName": "Jack"` — `=` for String? `Is` for reference? Structural equality for nested objects? For `"count": 5`, does the JSON number parse to `Integer` and compare numerically? `OPEN QUESTION`.
- **null 匹配**：Anthony's own 18.7 example writes `"line2": null`（事实）。What does `null` in a pattern mean — `Nothing`? JSON null distinct from missing? `OPEN QUESTION`.
- **嵌套对象/数组**：`{ "address": { "city": "London" } }` and `{ "tags": ["a","b"] }` — nested patterns and array patterns. Mainline's 2018.12.19 phasing put recursive patterns in Phase 2, and JSON shape matching is inherently recursive. `Probably` this feature cannot be Phase 1.
- **绑定形态**：the proposal shows no binding. Mainline's 2017.10.18 example binds `company`/`email` into the branch. If JSON patterns bind, we inherit every binding-scope question the ShapeOf family already owns; if they do not bind, the feature loses its dispatch value and degrades to a Boolean probe that duplicates `TypeOf`-style flow analysis. `OPEN QUESTION`.

**Q3. 作用域与绑定**

Nothing to decide yet because the proposal binds nothing. If we adopt the family binding model (scope of an introduced variable is the containing `Case` clause, per our ShapeOf resolution), JSON patterns inherit it wholesale. If `ShapeOf x Is { ... }` is used as an assertion, there is no binding at all — which is fine for tests but is the weaker half of the feature.

**Q4. 与既有特性交互**

- **`!` 字典访问**：`employee!emergencyContact` — mainline already worried "`!` means so much"（事实，2017.08.23），and in the same breath: "The JSON thing is great. Oh, but it already works with `!` so all the value just evaporated."（事实，2017.08.23）。`Dim x = y!name` already compiles as dictionary/default-property access（事实，2014-02-17），so on a dictionary-like JSON object `employee!emergencyContact` already works at runtime today. The schema type would change *IntelliSense*, not semantics. That collapses the schema half from "runtime feature" to "IDE feature" — which is exactly why PROPOSAL C (IDE-only) is competitive.
- **postfix cast**：Anthony composes `jsonObject!receivedDate(As Date)`（事实，第 3.12 章），so `!` access composes with the postfix casting proposal. Nice, but it is a combination of existing/new independent features, not a reason to rush the schema type.
- **JSON literals**：`{...}` is shared with `proposal-json-literals.md`（target-typed creation, `&=` writer chunks）。The pattern reading and the value reading must be disambiguated contextually, and the two proposals must be designed together. Today they are not.
- **ShapeOf family**：our ShapeOf resolution already reserved "形状匹配" for "解构 `Case (latitude, longitude)`、JSON 模式、用户定义模式方法、命名模式"（事实，`meeting-shapeof-pattern-matching.md`）。So JSON patterns *are* family. But the proposal's carrier — `ShapeOf newEmployee Is { ... }` — is precisely the standalone-operator form we tabled, and `Assert.IsTrue(ShapeOf ... Is ...)` is the expression form we told the family to express with `Matches`. The proposal contradicts our own resolution. `Probably` this is the proposal's biggest coordination debt.

**Q5. Breaking change**

No direct break: JSON shape patterns and schema annotations are new syntax; old code compiles unchanged. But the *operators* it leans on are not free: `ShapeOf` as a new keyword has identifier-collision risk (already flagged in the ShapeOf meeting), and `Is` overloaded to accept a pattern is the 2018.12.19 ambiguity concern again. `{"uri"}` as a new type position is additive but adds grammar the parser must special-case. Net: additive on the surface, operator-collision debt underneath — the same debt pattern we saw in the ShapeOf operator, just wearing JSON clothes.

**Q6. Option Strict / 编译选项分叉**

`ShapeOf newEmployee Is { ... }` with a statically-typed `newEmployee` (compile-time property mapping) versus an `Object`/late-bound payload (runtime dictionary matching) are two different implementations with different behaviors. Under Option Strict Off, VBScript.NET's default, the Object path would be the common one — and it is the reflection-free path only if the payload is a dictionary. If it is a late-bound object whose properties must be probed, we brush against the 2014 hard line: "this is impossible in the current CLR without reflection, and we wouldn't want a language feature that depended on reflection"（事实，2014-02-17）。The two Strict paths must behave identically; `Suspect` they cannot without a large spec.

**Q7. IDE/IntelliSense 影响**

The schema half is *for* the IDE, so the IDE story is the point, not a side effect. `jsonObject!` should complete against the schema; `employee!emergencyContact` should show `emergencyContact` in completion. But mainline's lean was that much of this is IDE-side work ("investigate with IDE team", 事实，2017.10.18). If the compiler only carries the annotation (like a comment) and the IDE does the completion, the compiler cost is near zero and the feature is PROPOSAL C. If the compiler must *resolve* the schema URI and build a type model from it, we need the URI-resolution answer (Q8/Q11) first.

**Q8. 数据/普遍性**

The strongest data is mainline's own framing — JSON is the cloud lingua franca; first-class JSON is an attractant for first-time developers（事实，2017.10.18）— and the VBScript heritage of runtime dispatch over dictionary data. The proposal adds no telemetry, no user requests, no real-world assertion that `Assert.AreEqual` ladders are a measurable pain. And mainline explicitly tabled JSON pattern matching *pending* feedback/scenarios（事实，2017.10.18）。Nothing in the proposal is new evidence; it re-argues the 2017 scenario. `Suspect`: for a VBScript.NET script library the scenario is genuinely common (HTTP payload checks are the bread-and-butter of scripting), but that is our engineering judgment, not data.

**Q9. 更简替代**

- **强类型反序列化**：`JsonSerializer.Deserialize(Of Employee)` gives typed access with full IntelliSense *without any language change*. For the assertion scenario this is a real competitor and, for typed payloads, probably better.
- **分析器/IDE**（PROPOSAL C）：validate shapes with analyzers, complete `!` access with IDE work, no compiler semantics.
- **手动 `JObject` 访问**：works, ceremony heavy, but zero risk.
- **`Select Case` 现有能力 + 布尔守卫**：`Case <boolExpr>` with `JObject` checks covers the dispatch case today, losing only ergonomics.
The only scenario where the language feature is clearly superior is *heterogeneous dispatch without typed fallback* — the 2017 `account!contacts` case. Everything else has a cheaper alternative. So the feature's value is real but narrow, and the proposal does not argue that narrowness.

**Q10. 成本/优先级**

JSON shape matching is a recursive/structural pattern — Phase 2 territory in the mainline phasing（declaration → recursive → and/or/not，事实，2018.12.19）。It cannot land before the family grammar settles `Case`/`Matches`/`When` and the binding model. The schema type, if it stays compiler-side, drags in URI resolution against the offline compile model the proposal itself concedes. If it goes IDE-side (C), the compiler cost collapses. `Probably`: high cost, Phase-2 slot, both halves blocked on prior decisions.

**Q11. 运行时/CLR 硬约束**

No CLR constraint for the pattern itself (it is compiler/type-model work). The two runtime paths are: (a) statically-typed subject — compile-time property mapping, no reflection; (b) dictionary subject — indexer access, no reflection. Only the late-bound-object probe path needs reflection, and we ruled that out in 2014（事实）。Schema resolution is a compile-time pipeline question, not a CLR one, but it collides with the offline model. No PEVerify concern.

**Q12. 值不值得做**

Value × Cost × Risk, scored separately for the two halves:

- **JSON shape pattern（分发）**：价值 7（VBScript.NET 异构负载分发是真实高频）/ 成本 6（递归模式 + 家族文法依赖 + `{...}` 消歧）/ 风险 5（`Is` 重载 + 新关键字）→ 值得做，但只能作为家族 Phase 2，且当前文档远不够格。
- **JSON schema 类型（`{"uri"}` + `!`）**：价值 4（IDE-only，`!` 运行期已可用）/ 成本 4–7（取决于编译器解析 vs IDE 层）/ 风险 4（离线模型张力 + 类型语法无先例）→ 值得考虑，但应走 annotated-types 线程、默认走 IDE 层。
- **捆绑成一个提案**：价值是两者之和的下界，成本与风险是两者之和的上界 → **捆绑本身就是净负**。We are confident the proposal should be split before it can be evaluated honestly.

---

### VB 基因对照

按设计原则 10 条逐条过：

1. **永不破坏现有代码** — 全部为新增语法，表面无破坏；`ShapeOf` 关键字与 `Is` 重载是既有债务（见 Q5）。
2. **保持 VB-like** — `Assert.IsTrue(ShapeOf newEmployee Is { ... })` 读起来不像 VB；主线的 `Case Match { ... }` 分发形态可读得多；`{"uri"}` 类型标注与 XML `<geo:Address>`（`proposal-xml-schema-types.md`）同源，`Probably` 可接受。
3. **不引入"第二种做事方式"** — 最大扣分点：`!` 字典访问已经能做 JSON 访问（2017.08.23："it already works with `!` so all the value just evaporated"）；再引入 schema 驱动访问是第二种方式。
4. **默认跟随 C#，除非有充分理由** — C# 没有 JSON 形状模式（System.Text.Json + `is` 属性模式不是一回事）；F# active patterns 被主线视为"仅作灵感"。无主线先例，属 Anthony 独立延伸，需要比"默认跟随"更强的理由。
5. **读起来像英语、对新手友好** — `ShapeOf newEmployee Is { "firstName": "Jack" }` 的 `Is` 读作"是"，但实际是"形状匹配于"；`Case Match { ... }` 读法更接近英语。
6. **不为边缘场景加特性** — JSON 分发不是边缘（API 响应、配置块），这是本建议最亮处；但"断言一个已知强类型返回值"的场景有更简替代（Q9），稀释了普遍性。
7. **避免隐蔽控制流/语义变化** — `!` 在 schema 类型变量上获得新含义，接近隐蔽语义变化；形状匹配本身是显式意图，无碍。
8. **不与既有语法冲突** — 三重冲突：`{...}`（字面量/初始化器）、`!`（字典访问）、`Is`（引用相等）。这是本建议最大的技术债务。
9. **消除常见样板** — 断言与分发的样板消除是真实价值，但量级弱于 `TypeOf` 收窄（同样的样板，零新语法）。
10. **冗长只在有用时是美德** — 无关键字形态（`Case { ... }` / `Matches`）优于操作符形态。

**主线对照（评价标准 2.3 表）**：主线对 JSON 一等支持是**热情但克制**——2017.10.18 明确 "strong attractant for first-time developers"，同时把 pattern 部分 "Table, wait for feedback/scenarios"；ModVB 本建议复活 pattern、新增 schema 类型。关系判定：**JSON 形状模式 = Anthony 独立延伸**（主线已 Table，无新数据）；**schema 类型 = 主线 annotated-types 讨论的延续**（2017.10.18 "XML and JSON" 一节，inactive 状态）；`ShapeOf ... Is` 操作符形态 = **与家族决议分叉**（我们的 ShapeOf 会议已将其 Table、改走 `Case`/`Matches`）。

**Breaking change 结论**：对现有合法代码无直接破坏；操作符/关键字层面有 `Is` 重载与 `ShapeOf` 新关键字的认知与标识符债务；`{"uri"}` 类型语法无先例但有新增语法成本。

---

### RESOLUTION

1. **场景成立，方向正确——但仅限分发形态。** 异构 JSON 负载的形状分发（2017.10.18 的 `account!contacts` 例）是真实痛点，且是唯一没有更简替代的场景；断言强类型返回值不是（`JsonSerializer` + 强类型即可）。JSON 形状模式是 ShapeOf 家族的合法成员——我们此前已把"JSON 模式"列入家族"形状匹配"名目（`meeting-shapeof-pattern-matching.md`）。

2. **本建议整体判定 `Table`，理由是结构性的而非价值的。**
   - **捆绑两个独立特性**：形状模式（运行时/编译期匹配）与 schema 类型（IDE 标注）机制不同、优先级不同、归属线程不同；捆绑使二者都无法被诚实评价。
   - **载体语法与家族决议冲突**：`ShapeOf x Is { ... }` 正是我们已 Table 的独立操作符形态；断言/表达式语境按家族决议应走主线 `Matches`。
   - **主线 2017.10.18 已 Table，且无新数据**：复活需要新证据，本建议没有提供。

3. **拆解后的去向：**
   - **JSON 形状模式 → Consider（家族 Phase 2 素材）**：作为递归/结构模式的一种字面量形态，并入家族统一文法；`Case { ... }` 分发形态 + `Matches` 断言形态；不引入新操作符。先回答 Q2 的匹配语义（大小写/属性映射、子集 vs 精确、null、值比较、嵌套/数组、顺序无关、绑定形态）。
   - **JSON schema 类型 → 移交 annotated-types 线程（inactive `proposal-annotated-types.md`），默认走 IDE 层**：`{"uri"}` 作为"注释而非类型"（如元组名、`Any` 之于 `Object` 的先例），编译器只携带标注、不解析 schema；`!` 保持现有字典访问语义，schema 只增强 IntelliSense。这尊重主线 "We might be able to do a lot with no compiler changes and should investigate with IDE team"。
   - **`ShapeOf ... Is` 操作符形态 → Reject**，与家族决议一致（改走 `Case`/`Matches`）。

4. **与同组建议的协调是先决条件**：`{...}` 的语法归属必须与 `proposal-json-literals.md`（目标类型创建、`&=` writer）一起裁决；`ShapeOf x Is {...}` 的载体形态必须与 `proposal-string-pattern-matching.md`（同一操作符形态）一起裁决。JSON 形状模式不能独自决定 `{...}` 的读法。

### Implication

- 家族统一文法将 `{...}` 列为一个模式字面量形态（递归/结构模式，Phase 2），并承接 Q2 的语义决策清单。
- annotated-types 线程承接 `{"uri"}` 与 `!` IDE 补全；编译器侧只做"注释"承载，不解析 schema URI。
- `proposal-json-pattern-matching.md` 拆为两份：形状模式（进家族）与 schema 类型（进 annotated-types），各自补足后再回到 Active/Consider。
- `TODO`（供返工）：与 `proposal-json-literals.md`、`proposal-string-pattern-matching.md` 的 `{...}` / 操作符形态对表；Q2 全部语义决策规范；兼容性/`Option Strict` 双路径验证；原型分支与运行结果（状态栏占位链接待补）。

---

### 三态判定

- **本建议（整体）**：`Table` — 捆绑两特性、载体语法与家族决议冲突、主线已 Table 且无新数据。
- **分解后**：
  - JSON 形状模式（分发形态）→ **Consider**（家族 Phase 2，经归并后）；
  - JSON schema 类型（`{"uri"}` + `!` IDE 补全）→ **Table**，移交 annotated-types 线程、默认 IDE 层；
  - `ShapeOf ... Is` 操作符形态 → **Reject**（与家族决议一致）。

**后续动作**：① 通知作者拆分为两份并归并家族文法与 annotated-types 线程；② 把 Q2 语义决策清单交给家族统一语法起草；③ 与 json-literals / string-pattern 会议对表 `{...}` 与操作符载体；④ 标注主线 2017.10.18 决议与 #139 XML Patterns 先例。

---

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTIONS`（匹配语义，供家族文法）：大小写与属性映射；子集 vs 精确匹配；字段顺序无关；值比较（`=`/`Is`/结构/数值解析）；`null` vs `Nothing` vs 缺失；嵌套对象/数组模式；绑定形态（绑定 vs 纯布尔断言）。
- `OPEN QUESTIONS`（schema 类型）：`{"uri"}` 类型语法能否落地（无先例，2017.10.18 未决）；schema URI 解析离线张力（编译器解析 vs IDE 层）；`!` 在 schema 变量上的语义边界。
- `TODO`：把 `{...}` 五种读法（数组/集合/匿名/JSON 字面量/JSON 模式/schema 标注）列成统一消歧表；补原型与数据。
- `Follow-up`：与 `proposal-json-literals.md`、`proposal-string-pattern-matching.md`、`proposal-annotated-types.md`（inactive）、家族文法四方对表。

---

## 附录：特性评价

### 评价对象
- 建议：`proposal-json-pattern-matching.md`（JSON 模式匹配）
- 来源：Anthony D. Green 原文第 5 章 "JSON and JSON Pattern Matching"（另涉 18.7 反序列化守卫、3.12 postfix cast 组合）
- 配方目标：用 JSON 形状直接描述期望（`ShapeOf expr Is { ... }`），配合 JSON Schema 类型（`{"uri"}` + `!`）为匿名/动态数据附加类型信息，获得断言简化与 IntelliSense。

### 五维评价

| 维度 | 得分 | 评价（行为锚点对照） | 证据等级 | 存在问题 |
|------|------|----------------------|----------|----------|
| 效果 | 3/5 | 锚点≈"只覆盖部分场景"。目标改进明确（断言/分发 + IDE），示例来自原文可演示；但证据止于书面、无原型；关键子效果全部缺失（大小写/非字符串值/嵌套/数组/子集匹配/null/绑定），未决问题 ≥4 个 → 核心语法未定型，效果封顶 3–4。schema 类型的 IntelliSense 效果纯属预测。 | 已检查 | 无原型；核心匹配语义未定；捆绑两特性稀释可测性 |
| 特性 | 2/5 | 锚点≈"外来特性直接照搬未 VB 化；或多个强无关能力捆绑"。`{...}` 与集合/匿名/数组初始化器撞语法；`!` 与字典访问撞语义（运行期"已经能用"）；`ShapeOf ... Is` 与家族决议冲突；schema 类型实为另一份建议（annotated-types）的内容。捆绑 = 强无关能力捆绑。 | 已检查 | 双重捆绑；三重语法/语义冲突面；`ShapeOf` 操作符形态与家族分叉 |
| 品质 | 3/5 | 锚点≈"缺某一章节或在关键处边界含糊"。六章节齐全、示例与原文一致、4 个未决问题诚实列出（未直接扣分）；但无文法/BNF、无角案例小节、无兼容性/Option Strict/IDE 分析；Drawbacks 只列 3 点且未逐一反驳；schema 部分实质是另一份建议的内容。状态栏为占位链接（项目惯例，不计重）。 | 已检查 | 缺 Grammar/Edge cases/Compatibility；捆绑未拆；未标注主线 2017.10.18 决议 |
| 属性 | 3/5 | 锚点≈"有得有失，文档未充分权衡"。对水（JSON 首次开发者磁铁）、光（VBScript.NET 脚本 JSON 差异化）正向；暗风险突出：schema URI 拉取 vs 离线编译张力、`{...}`/`!`/`Is` 三重冲突、与主线 Table 决议张力、捆绑导致一致性断裂（风）——文档只在 Drawbacks 各提一句，未充分权衡。 | 已检查（待定，预测性） | 离线模型张力未识别为风险；主线 Table 决议未处理；冲突未分析 |
| 炼金成分 | 3/5 | 锚点≈"部分来源未标注；标注与影响有偏差"。来源标注准确（Anthony 第 5 章逐字示例）；但未标注主线 2017.10.18 JSON pattern Table 决议与 #139 XML Patterns 先例；schema 类型血缘（主线 annotated-types 讨论 → inactive annotated-types 建议）未交叉；`!` 与主线 "`!` means so much"（2017.08.23）讨论未交叉；未提 Anthony 原文的 demo 视频（第 5 章行首链接）。 | 已检查 | 主线先例未标注；特性血缘未交叉标注 |

### 设计原则对照
- **与 VB 基因**：部分偏离。JSON 一等支持符合"简单/低仪式/首次开发者"定位（原则 #5/#9 方向），但操作符形态（`ShapeOf ... Is {...}`）、`!` 语义扩展（#3 第二种做事方式、#7 隐蔽语义变化）、`{...}` 语法冲突（#8）构成实质偏离。
- **与主线关系**：JSON 一等支持 = **主线一致方向**（2017.10.18 热情但 Table）；JSON 形状模式 = **Anthony 独立延伸**（主线已 Table，无新数据）；schema 类型 = **主线 annotated-types 讨论的延续**（inactive）；`ShapeOf` 操作符形态 = **与家族决议分叉**。
- **破坏性变更**：对现有合法代码无直接破坏；有操作符/关键字层面债务（`Is` 重载、`ShapeOf` 新关键字）与 `{"uri"}` 类型语法新增成本。

### 总评
- **达成程度**：**部分达成** — 场景（异构负载形状分发）成立且无更简替代；但捆绑两特性、载体语法与家族决议冲突、核心匹配语义未定义、证据止于书面。
- **LDM 三态建议**：整体 **Table**；分解后 JSON 形状模式 → **Consider**（家族 Phase 2），schema 类型 → **Table**/移交 annotated-types（默认 IDE 层），`ShapeOf ... Is` 操作符形态 → **Reject**。
- **主要问题**：① 捆绑两个独立特性（形状模式 + schema 类型）；② 核心匹配语义（Q2 七项）全未定义；③ `{...}`/`!`/`Is` 三重语法冲突未分析；④ 主线 2017.10.18 Table 决议未引用且无新数据；⑤ 载体语法与家族决议直接冲突。

### 返工建议
- **拆分建议**：拆为 (a) JSON 形状模式（进家族文法）与 (b) JSON schema 类型（进 annotated-types 线程）；拆分后分别评价。
- **补充章节**：`Grammar/BNF`（`{...}` 模式文法 + `Is` 消歧 + `Case { ... }`/`Matches` 载体）；`Edge cases`（大小写/属性映射、子集 vs 精确、顺序无关、值比较、null、嵌套/数组、绑定形态）；`Compatibility` 分析；`Option Strict` 双路径验证；IDE 影响；与 `proposal-json-literals.md`、`proposal-string-pattern-matching.md`、`proposal-annotated-types.md`（inactive）的消歧表。
- **补充证据**：主线 2017.10.18 决议与 #139 XML Patterns 对比表；annotated-types 血缘标注；原型分支与运行结果（替换占位链接）；异构负载分发场景的量化数据。
- **未决问题处理**：把现有 4 个未决问题升级为 Q2 语义决策清单；新增：`{...}` 五种读法消歧、`!` 语义边界、schema URI 离线解析策略（编译器 vs IDE 层）。

---

## 附录：C# 生态与互操作考量

> 本附录把本建议（JSON 模式匹配，List/JSON 模式）放到 C#/CLR/.NET 生态的现实方向里核对。主要依据 `..\..\csharplang`（dotnet/csharplang 官方镜像）与浓缩索引 `..\..\csharplang-index.md`（T8/M4）。C# 原文逐字引用并标注来源路径；无法核实的标 **OPEN QUESTIONS**。诚实声明：本建议与 C#「低层互操作」（Span/ref/unsafe，索引 T2/T3）关系弱，本附录不硬凑该主线；真正的接口面是**模式匹配语言线**与 **System.Text.Json / 元数据 / AOT** 的生态面。

### 相关 C# 现实方向：模式匹配是 C# 已完成并持续扩写的能力线

C# 的模式匹配不是单一特性，而是一条从 C# 7 铺到 C# 15 的能力线。本建议的两个要素（对象形状、数组形状）都能在 C# 线上找到对应：

- **C# 7.0**：`is`/`switch` 上的常量与类型模式（`proposals\csharp-7.0\pattern-matching.md`）。
- **C# 8.0**：递归模式（property/positional patterns）落地。Summary 原文逐字：「Pattern matching extensions for C# enable many of the benefits of algebraic data types and pattern matching from functional languages, but in a way that smoothly integrates with the feel of the underlying language.」→ `proposals\csharp-8.0\patterns.md`（Summary）。同文件把递归定性：「Patterns may be recursive so that parts of the data may be matched against sub-patterns.」→ 同上（Detailed design）。property pattern 定义逐字：「A property pattern checks that the input value is not `null` and recursively matches values extracted by the use of accessible properties or fields.」→ 同上（Property pattern）。
- **C# 9.0**：组合子 `and`/`or`/`not`/relational/parenthesized（`proposals\csharp-9.0\patterns3.md`；Language-Version-History 记为 "combinator patterns (`is >= 0 and <= 100`, `case 3 or 4:`, `is not null`)"）。
- **C# 11.0**：列表模式。Summary 原文逐字：「Lets you to match an array or a list with a sequence of patterns e.g. `array is [1, 2, 3]` will match an integer array of the length three with 1, 2, 3 as its elements, respectively.」→ `proposals\csharp-11.0\list-patterns.md`（Summary）。文法含 `list_pattern_clause`（`'[' (pattern (',' pattern)* ','?)? ']'`）与 `slice_pattern`（`'..' pattern?`，丢弃 "zero or more" 个元素）；兼容规则为 countable + indexable（`Length` + `int`/`Index` 索引器），slice 需 `Range` 索引器或 `Slice(int,int)`。多维数组明确不支持（决议 `meetings\2021\LDM-2021-05-26.md`，经该文件核实存在）。
- **C# 12.0**：扩展属性模式（实验于 `proposals\csharp-10.0\extended-property-patterns.md`，落地 C# 12）。Motivation 原文逐字：「When you want to match a child property, nesting another recursive pattern adds too much noise which will hurt readability with no real advantage.」→ 同上（Motivation）。把 `{ Method: { Name: "x" } }` 压平为 `{ Method.Name: "x" }`。
- **C# 生态的 JSON 答案在运行时库而非语言**：`System.Text.Json`（dotnet/runtime 生态，本库无正文）提供反序列化到强类型（`JsonSerializer.Deserialize<T>`）与 DOM（`JsonElement`/`JsonNode`）；.NET 8 起 source-generated 序列化器（`JsonSerializerContext`/`[JsonSerializable]`）为 AOT/trimming 服务。C# 语言侧**从未**把 JSON 形状做成语言特性。
- **C# 15 方向（unions/closed hierarchies）**：类型系统走向「一组封闭类型」以服务模式匹配与 AOT（索引 T8/M4）。封闭类用新元数据表达，原文逐字：「Closed classes shall not be inherited from languages that do not support closed classes. This is accomplished by adding `[CompilerFeatureRequired("ClosedClasses")]` to all constructors of closed classes.」→ `proposals\closed-hierarchies.md`（inheritance rules）。`CompilerFeatureRequired` 亦用于 C# 11 required members（`proposals\csharp-11.0\required-members.md`）。
- **字典键匹配在 C# 是空白，但未来方向已声明**：`proposals\dictionary-expressions.md`（in-progress，champion #8659，尚未归档到具体版本）明言 C# 现有字典**没有**解构/模式对应物，原文逐字：「Unlike with *collection expressions*, C# does not have an existing pattern serving as the corresponding deconstruction form.」→ 同上（Motivation）；并声明未来意图：「It should also feel pleasant in the language, complement the work done with collection expressions, and naturally extend to pattern matching in the future.」→ 同上（Motivation）。

### 现实 vs 提案

| 提案要素 | C# 现实 | 关系判定 |
|---|---|---|
| `ShapeOf expr Is { "firstName": "Jack" }` 对象形状 | C# 8 属性模式 `expr is Employee { FirstName: "Jack" }`——**成员导向**；对 `Dictionary<string,object>`/`JsonElement` **无按键模式**（见上 dictionary-expressions 原文） | **部分兼容、需桥接**：形状语义同构，但 C# 匹配静态成员、提案匹配运行期键；C# 对动态键无对应，恰是提案可差异化的空白 |
| `{ "address": { "city": "London" } }` 嵌套对象 | C# 8 递归属性模式 + C# 12 扩展属性模式压平嵌套 | **兼容**：C# 12 动机原文（嵌套递归模式噪声大）与本提案嵌套形状诉求同源 |
| `{ "tags": ["a","b"] }` 数组形状 | C# 11 列表模式 `[ "a", "b" ]` + slice `..` | **兼容**：JSON 数组形状与 list pattern + slice 一一对应 |
| `Case Match { "type": "business", ... }` 异构分发 | C# switch 表达式 + C# 15 closed hierarchies/unions | **同向、机会**：C# 用封闭类型集服务分发，提案用开放形状服务分发，一封闭一开放，互为镜像 |
| `Assert.IsTrue(ShapeOf ... Is {...})` 断言 | C# `is` 模式 + System.Text.Json 反序列化到强类型再模式匹配 | **需桥接**：C# 生态的 JSON 答案 = 运行时库反序列化 + 类型化模式，而非语言层 JSON 形状；印证 meeting Q9「强类型反序列化是更简替代」 |
| `{"uri"}` schema 类型（IDE 标注） | C# 无对应语言特性；字典字面量 `["k": v]`（future dictionary-expressions）是最接近的语法 | **脱节**：C# 生态无 JSON schema 语言标注先例，annotated-types 线程无 C# 参照 |
| `{...}` 在模式语境 | C# 固定为属性模式定界符 `{}`；列表模式用 `[]`；字典字面量（future）用 `["k": v]` | **借鉴**：C# 以**不同定界符**规避了 VB `{...}` 五读问题，可纳入家族消歧表 |

**判定**：本提案与 C# 模式能力线**大部分兼容**（对象/数组形状均能找到 C# 对应），但存在一个真实**空白**——C# 对动态键（dictionary/JSON 键）无形状匹配，C# 生态的 JSON 答案在运行时库而非语言。因此「键导向 JSON 形状匹配」目前**无主线先例**（印证 meeting「Anthony 独立延伸」判定），但它不是与 C# 冲突——它是 C# 尚未覆盖的相邻地带，且 C# 已声明未来会向字典模式延伸（见上）。

### 对 VBScript.NET 的适应建议

1. **默认安全 / 按需动态（呼应 Q6 双路径）**：`{...}` 形状模式应对 typed subject（编译期成员映射，对应 C# 属性模式）与 dictionary/`JsonElement` subject（运行期键匹配）行为一致。C# 列表模式提供先例：模式不依赖具体类型、只依赖能力（countable + indexable 的鸭子类型）。VB 形状模式可仿照「能力导向」——对「可键控」类型走键匹配、对 typed 类型走成员匹配，两条路径同语义、不同实现。
2. **source-gen 桥**：C# 生态 JSON 主路径是 System.Text.Json +（.NET 8 起）source-generated 序列化器。VBScript.NET 若默认「编译到受管程序集」（决策文件 M5/T6 的脚本出口），应识别 `JsonSerializerContext`/`[JsonSerializable]` 生成元数据，让脚本的 JSON 反序列化走编译期生成、产物再喂给 `{...}` 模式——同时服务 AOT/trimming。
3. **识别新元数据**：C# 15 closed hierarchies 以 `[CompilerFeatureRequired("ClosedClasses")]` 封闭类型（逐字见上），required members 用 `[CompilerFeatureRequired("RequiredMembers")]`。VB 编译器须识别这些特性才能正确消费 C# 15 类型（派生封闭类被拒、模式匹配可对齐 C# 穷尽性分析）。若反序列化目标类型是 C# 封闭层级，VB 的模式分发应与 C# 的封闭判定一致。此点呼应决策文件 M4 的桥接提醒。
4. **`{...}` 消歧借鉴 C# 分符策略**：C# 以 `{}`=属性模式、`[]`=列表模式、`["k": v]`=字典字面量（future）三分。VB 家族文法可考虑让「JSON 对象形状」用 `{ "k": v }`、「JSON 数组形状」用 `[v, ...]`（对齐 C# list pattern 语法），在消歧表里把对象形状与数组形状分开，减轻 `{...}` 五读负担（对接正文 TODO）。
5. **跟踪 C# 字典模式方向**：`proposals\dictionary-expressions.md` 明确未来要 "naturally extend to pattern matching"。若 C# 未来落地字典键模式，本提案的「键导向形状匹配」将从「独立延伸」变为「C# 同向」，届时应重估三态。此为 **Follow-up / OPEN** 跟踪点。

### 对既有 RESOLUTION / 三态判定的影响

- **无冲突，基本不变**。RESOLUTION 的拆解判断（形状模式→家族 Phase 2；schema 类型→annotated-types、默认 IDE 层；`ShapeOf ... Is` 操作符形态→Reject）与 C# 现实不冲突；C# 无 schema 类型、无字典键模式的现实，不改变「无主线先例 → 需更强理由」的判定。
- **C# 现实强化 Phase 2 成本判断**：C# 从 C#8（递归）→C#9（组合子）→C#11（列表）→C#12（扩展属性）四个版本才铺满模式能力；递归/结构/组合模式确属 Phase 2 成本级。本建议「JSON 形状模式只能作为家族 Phase 2」与 C# 节奏吻合。
- **C# 空白是双刃**：无字典键模式 = 无设计参照（责任全在 VB 侧，印证独立延伸判定）；同时是 VB 可先于 C# 提供键导向形状匹配的差异化空间。
- **一处措辞建议**：RESOLUTION #3 把 JSON 形状模式列为「递归/结构模式的一种字面量形态」。对照 C# 现状，更精确的定位是「**键导向**结构模式」——对象形状=键匹配（C# 无）、数组形状=list pattern 同构（C# 有）。C# 已覆盖「成员导向」与「顺序导向」，唯独缺「键导向」；建议家族文法起草时用此定位与 C# 对表。

### 引用纪律

- 本附录引用的 C# 原文均逐字摘自 `..\..\csharplang` 并在正文标注来源：`proposals\csharp-8.0\patterns.md`、`proposals\csharp-9.0\patterns3.md`、`proposals\csharp-11.0\list-patterns.md`、`proposals\csharp-10.0\extended-property-patterns.md`、`proposals\dictionary-expressions.md`、`proposals\closed-hierarchies.md`、`Language-Version-History.md`。`meetings\2021\LDM-2021-05-26.md`（列表模式决议）经核实存在。
- **OPEN QUESTIONS**：System.Text.Json 的 source-gen 元数据具体形态属 dotnet/runtime 生态，本库无正文，未逐一核实（按索引 T5/T6 定性引用）；dictionary-expressions 是否进入 C# 14/15 里程碑未核实（in-progress，champion #8659）；C# 15 unions 对模式匹配穷尽性的精确语言规则未核实（见 `meetings\working-groups\discriminated-unions\`）。
