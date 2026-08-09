# ModVB 语法建议索引

本文档从 Anthony D. Green 的《The Agenda – A Visual Guide》（`../AnthonyDesign_wordpress.txt`）拆解而来，按 vblang 规范组织为「建议 → 会议 → spec」三层结构。本目录存放**建议（proposals）**层，每个独立新语法对应一份建议文档。

## 建议状态（四类，仿 csharplang 组织）

| 状态 | 位置 | 含义 |
|------|------|------|
| **active** | `proposals/` 根目录 | 设计在推进（RESOLUTION **Active** / **Consider**），准备进入实现 |
| **inactive** | `proposals/inactive/` | 有前景但暂不优先 / 未定型（RESOLUTION **Table**） |
| **rejected** | `proposals/rejected/` | 否决（RESOLUTION **Reject**） |
| **done** | `proposals/vbscript-<版本>/` | 已随 VBScript.NET 发布版本实现并定稿（当前无成员，机制见 `vbscript-1.0/README.md`） |

- **判定依据**：各会议纪要「三态判定」小节（`../meetings/`，与提案 1:1 同名镜像组织）。
- **分类反映当前设计意图**，不排斥后续复活（inactive/rejected 可因信号回升）或归档（active 完成后转 done）。
- 会议纪要目录与提案目录同步组织：`meetings/` ↔ `proposals/`、`meetings/inactive/` ↔ `proposals/inactive/`、`meetings/rejected/` ↔ `proposals/rejected/`。
- 状态行（模板顶部）：`Proposed / Prototype / Implementation / Specification` 复选框标记进度。

---

## Active（37 份，根目录）

> RESOLUTION = Active 或 Consider；编号沿用 Anthony 原文章节顺序。

| # | 文件名 | 建议 |
|---|--------|------|
| 01 | `proposal-typeof-flow-analysis.md` | `TypeOf Is/IsNot` 流敏感类型收窄（**Active**） |
| 02 | `proposal-nullability-flow-analysis.md` | 可空性流分析，轨道1 值类型（**Active**/轨道2 Table） |
| 03 | `proposal-conditional-best-common-type.md` | `If()` 最近公共基类型推断（Consider） |
| 04 | `proposal-recursive-lambda-inference.md` | 递归/互递归 Lambda 类型推断（Consider） |
| 05 | `proposal-top-level-code.md` | 顶级代码（沉浸式文件，窄子集候选 Active） |
| 06 | `proposal-key-fields-auto-constructors.md` | `Key` 字段自动构造（Consider） |
| 07 | `proposal-wildcard-lambdas.md` | 通配符 Lambda `*.Member`（Consider） |
| 08 | `proposal-abbreviated-properties-events.md` | 简写属性/事件，`Return` 表达式体（**Active**） |
| 11 | `proposal-implicit-line-continuations.md` | 新增隐式行继续（Consider） |
| 12 | `proposal-minor-fixes.md` | 杂项修复，1/2/4/7 Active（Async Main 等） |
| 13 | `proposal-local-declarations.md` | `Let` 声明 + 元组解构 + `As New` 数组（Consider） |
| 16 | `proposal-for-enhancements.md` | `For` 多计数器/Exit 指定层（Consider） |
| 17 | `proposal-for-each-enhancements.md` | `For Each` 解构/Where/`Await Each`（Consider） |
| 18 | `proposal-do-enhancements.md` | `Do` 循环头先赋值再测条件（Consider） |
| 19 | `proposal-with-enhancements.md` | `With` 命名变量、`.Me`（Consider） |
| 21 | `proposal-try-enhancements.md` | `Try` 内 Await（Catch/Finally，**Active**） |
| 24 | `proposal-return-byref.md` | `Return` 写回 ByRef/Out 参数（**Active**） |
| 28 | `proposal-json-literals.md` | JSON 数据字面量（Consider） |
| 36 | `proposal-out-arguments.md` | `Out` 实参/只写形参（**Active**） |
| 38 | `proposal-method-level-imports.md` | 方法级 `Imports`（Consider） |
| 40 | `proposal-delegate-enhancements.md` | 委托 `+=`/`-=` + 匿名委托类型（Consider） |
| 43 | `proposal-query-enhancements.md` | 查询增强，目标类型化 `Select`/BC36606（Consider） |
| 46 | `proposal-initializer-enhancements.md` | 初始化器增强，C 项 Active（Consider） |
| 48 | `proposal-any-pseudotype.md` | `Any` 伪类型（晚期绑定差异化，Consider） |
| 50 | `proposal-default-methods.md` | `Default` 方法（可调用对象，Consider） |
| 54 | `proposal-async-iterator.md` | `Await Each` / `Async Iterator`（消费端先落地，Consider） |
| 63 | `proposal-partial-members.md` | `Partial` 成员（Consider） |
| 64 | `proposal-semantic-preprocessing.md` | `##If` 语义预处理（Consider） |
| 65 | `proposal-intersection-union-types.md` | 交集/并集类型（交集限局部变量，Consider） |
| 67 | `proposal-date-time-literals.md` | 日期/时间字面量，毫秒（**Active**） |
| 68 | `proposal-override-signature-relaxation.md` | 覆写返回类型协变（Consider，范围收缩挂起） |
| 69 | `proposal-implicit-interface-implementation.md` | 隐式接口实现，协变返回 Active（部分采纳） |
| 70 | `proposal-interface-delegation.md` | 接口实现委托给字段/属性（Consider） |
| 71 | `proposal-extension-properties.md` | 扩展属性，v1 只读（Consider） |
| 72 | `proposal-name-resolution.md` | 名称解析 BC30456 回退（Consider） |
| 73 | `proposal-initonly-mustinit.md` | `InitOnly`/`MustInit` 属性（Consider） |
| 98 | `proposal-scripting-interpreted.md` | 脚本化记录：脚本宿主 + REPL + 顶层代码（Consider） |

---

## Inactive（60 份，`inactive/`）

> RESOLUTION = Table（搁置/未定型）。含原章 18 实验想法与从根目录下沉的 Table 提案。

| # | 文件名（均带 `inactive/` 前缀） | 建议 |
|---|--------|------|
| 09 | `proposal-markdown-doc-comments.md` | Markdown 文档注释 |
| 10 | `proposal-ignore-warning-directive.md` | `#Ignore Warning` 指令 |
| 14 | `proposal-set-statement.md` | `Set` 赋值语句（解构赋值 Consider） |
| 15 | `proposal-select-case-enhancements.md` | `Select Case` 增强（并入模式家族） |
| 20 | `proposal-throw-inference.md` | `Throw` 异常类型推断 |
| 22 | `proposal-using-synclock-enhancements.md` | `Using`/`SyncLock` 增强（解构头 Active） |
| 23 | `proposal-postfix-casting.md` | 后置转换 `(As Type)`（D3 已定型语义） |
| 25 | `proposal-xaml-literals.md` | XAML 字面量（source-gen 契约） |
| 26 | `proposal-embedded-vb-mode.md` | 嵌入 VB 解析模式 `<?vb?>` |
| 27 | `proposal-xml-schema-types.md` | XML Schema 类型（注释变体 Consider） |
| 29 | `proposal-json-pattern-matching.md` | JSON 模式匹配（形状模式 Consider） |
| 30 | `proposal-string-pattern-matching.md` | 字符串模式匹配（B 子集 Consider） |
| 31 | `proposal-interpolated-string-optimization.md` | 插值字符串优化（B→Active） |
| 32 | `proposal-string-narrowing-conversions.md` | 字符串窄化转换（枚举窄化 Consider） |
| 33 | `proposal-shapeof-pattern-matching.md` | `ShapeOf` 模式匹配（归家族 Phase 1 Active） |
| 34 | `proposal-user-defined-pattern-methods.md` | 用户定义模式方法（归家族 Phase 2 Active） |
| 35 | `proposal-named-patterns.md` | 命名模式（概念 Active） |
| 37 | `proposal-module-enhancements.md` | 模块增强（泛型非提升 Consider） |
| 39 | `proposal-bit-enum.md` | `Bit Enum` 位枚举（声明侧 Consider） |
| 42 | `proposal-range-expressions.md` | 范围表达式 `1 To 10 Step 2`（迭代源 Consider） |
| 44 | `proposal-query-comprehensions.md` | 新查询理解（`Include` 关联加载） |
| 45 | `proposal-insert-update-delete-expressions.md` | `Insert`/`Update`/`Delete` 表达式 |
| 47 | `proposal-in-notin-operators.md` | `In`/`NotIn` 运算符（映射 `Contains`） |
| 49 | `proposal-typeless-declarations.md` | 无类型声明默认 `Any`（收进脚本模式） |
| 52 | `proposal-agile-async.md` | `Agile Async` / `Await` 省略 |
| 56 | `proposal-null-literal.md` | `Null` 字面量与可空推断 |
| 57 | `proposal-null-equality-operators.md` | 二值逻辑相等 `?=`/`?<>` |
| 58 | `proposal-null-coalescing.md` | 后置空合并 `??`（`True?` Consider） |
| 59 | `proposal-do-nothing.md` | `Do Nothing` 空操作语句 |
| 61 | `proposal-smart-attributes.md` | 智能属性（`PropertyHandlerAttribute`） |
| 62 | `proposal-replacement-modifiers.md` | `Replaceable`/`Replaces`/`MustReplace`（MustReplace 种子 Consider） |
| 66 | `proposal-array-pseudotype.md` | 数组伪类型 `Array(Of T)`（按大小实例化 Consider） |
| 74 | `proposal-runtime-library.md` | VB 运行时库改进（性能 Active） |
| 75 | `proposal-target-typed-conversions.md` | 目标类型化转换 |
| 76 | `proposal-guarded-let.md` | Guarded `Let`（`Let ... Then/Else/End Let`） |
| 77 | `proposal-case-else-variable.md` | `Case Else` 变量 |
| 78 | `proposal-exclusive-for-upper-bound.md` | 独占上界 `To <` |
| 79 | `proposal-parallel-extensions.md` | 并行扩展 |
| 80 | `proposal-retry-resume.md` | `Retry`/`Resume` |
| 81 | `proposal-robust-mapping.md` | 更健壮的映射（null 键） |
| 82 | `proposal-json-serializers.md` | VB 惯用 JSON 序列化器 |
| 83 | `proposal-case-insensitivity.md` | 大小写不敏感/排序规则集成 |
| 84 | `proposal-string-pattern-lookahead.md` | 字符串模式前瞻/回溯 |
| 85 | `proposal-string-span-utf8.md` | `String` 与 `Span`/UTF-8 |
| 86 | `proposal-named-pattern-inputs.md` | 命名模式输入 |
| 87 | `proposal-patterns-as-data.md` | 模式作为数据 |
| 88 | `proposal-plinq-async-queries.md` | PLINQ / 异步查询 |
| 89 | `proposal-configureawait-options.md` | `ConfigureAwait` 选项与未观测异常 |
| 90 | `proposal-nullable-reference-types.md` | 可空引用类型（切片1 Active） |
| 91 | `proposal-duplicate-declarations.md` | 重复声明处理 / `Appendable` |
| 92 | `proposal-generative-compiler-scripting.md` | 生成式编译器脚本（作者自评坏主意） |
| 93 | `proposal-units-of-measure.md` | 度量单位 |
| 94 | `proposal-annotated-types.md` | 注释类型 / 类型变体 |
| 95 | `proposal-case-classes.md` | `Case` 类/结构/接口 |
| 96 | `proposal-structure-constraint-overloading.md` | `Structure` 约束重载 |
| 97 | `proposal-native-instruction-helpers.md` | 平台原生指令辅助 |
| 99 | `proposal-chained-ternary.md` | 链式三元表达式（`? If()` Reject） |
| 100 | `proposal-type-predicates.md` | 类型谓词（流敏感类型） |
| 101 | `proposal-versioning-tools.md` | 版本化工具 / `Imports Wpf` |
| 102 | `proposal-rust-ownership.md` | 任意块作用域、Rust 式所有权（所有权 Reject） |

---

## Rejected（5 份，`rejected/`）

> RESOLUTION = Reject（语言特性否决；部分含分析器/窄方案子裁定）。

| # | 文件名（均带 `rejected/` 前缀） | 建议 | 子裁定 |
|---|--------|------|--------|
| 41 | `proposal-pipeline-operator.md` | 管道运算符 `->` | 窄方案 Consider |
| 51 | `proposal-async-sub.md` | `Async Sub` 返回类型签名 | — |
| 53 | `proposal-require-await-call.md` | 强制 Await / `Call` | 官方分析器 Active |
| 55 | `proposal-async-event.md` | `Async Event` | 库方案覆盖 |
| 60 | `proposal-null-safe-behaviors.md` | 空安全行为（`?.` 语句层） | 两窄子集 Consider |

---

## Done（暂无成员）

- 机制：特性随 VBScript.NET 发布版本实现并定稿后，归档到 `proposals/vbscript-<版本>/`。
- 当前占位：`vbscript-1.0/README.md` 说明归档规则；VBScript.NET 尚无发布特性。

---

## 章节对照（Anthony 原文）

按原文 18 章分组视图见 `../modvb-index.md`；本 README 按四类状态组织。编号沿用原文章节顺序，便于追溯。
