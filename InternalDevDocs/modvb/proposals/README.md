# ModVB 语法建议索引

本文档从 Anthony D. Green 的《The Agenda – A Visual Guide》（`../AnthonyDesign_wordpress.txt`）拆解而来，
按 vblang 的规范组织为"建议 → 会议 → spec"三层结构。本目录存放**建议（proposals）**层，
每个独立的新语法对应一份建议文档。

- **建议状态约定**：与 vblang 一致，用复选框标记 `Proposed / Prototype / Implementation / Specification` 进度。
- **`inactive/` 子目录**：存放 Anthony 明确标注为"还需要时间酝酿"（第 18 章）的实验性想法。
- **会议与 spec**：待建议定稿后另行撰写，目录已占位（`../meetings`、`../spec`）。

> 说明：本索引已与实际文件核对一致（74 份核心建议 + 28 份实验性建议 = 102 份）。
> 个别原文细项因内容相近已合并（见各章标注），索引以实际落盘文件为准。

## 建议清单（按原文章节分组）

### 1. 类型推断增强
| # | 文件名 | 建议 |
|---|--------|------|
| 01 | `proposal-typeof-flow-analysis.md` | `TypeOf ... Is` 类型流分析（If/Select Case/守卫语句中的类型收窄） |
| 02 | `proposal-nullability-flow-analysis.md` | 可空性流分析（`IsNot Null` 收窄） |
| 03 | `proposal-conditional-best-common-type.md` | 条件表达式（`If()`）最佳公共类型推断 |
| 04 | `proposal-recursive-lambda-inference.md` | 递归 / 互递归 Lambda 的类型推断 |

### 2. 精简与样板消除
| # | 文件名 | 建议 |
|---|--------|------|
| 05 | `proposal-top-level-code.md` | 顶级代码（沉浸式文件 "Immersive Files"） |
| 06 | `proposal-key-fields-auto-constructors.md` | `Key` 字段/属性与自动构造函数 |
| 07 | `proposal-wildcard-lambdas.md` | 通配符 Lambda 表达式（`*.Url`） |
| 08 | `proposal-abbreviated-properties-events.md` | 简写属性/事件与条件属性 |
| 09 | `proposal-markdown-doc-comments.md` | Markdown 文档注释语法 |
| 10 | `proposal-ignore-warning-directive.md` | `#Ignore Warning` 指令 |
| 11 | `proposal-implicit-line-continuations.md` | 新增隐式行继续 |
| 12 | `proposal-minor-fixes.md` | 杂项修复（Async Main、`Optional` 默认值推断等） |

### 3. 语句与表达式现代化
| # | 文件名 | 建议 |
|---|--------|------|
| 13 | `proposal-local-declarations.md` | 局部变量声明增强（元组解构、`As New` 数组/匿名类型） |
| 14 | `proposal-set-statement.md` | `Set` 赋值语句 |
| 15 | `proposal-select-case-enhancements.md` | `Select Case` 增强（TypeOf/ShapeOf/Is/Like/In） |
| 16 | `proposal-for-enhancements.md` | `For` 增强（多计数器、显式变量、迭代捕获修复） |
| 17 | `proposal-for-each-enhancements.md` | `For Each` 增强（解构、查询、`Await Each`） |
| 18 | `proposal-do-enhancements.md` | `Do` 循环头声明/赋值 |
| 19 | `proposal-with-enhancements.md` | `With` 增强（命名变量、`.Me`、复合赋值） |
| 20 | `proposal-throw-inference.md` | `Throw` 异常类型推断 |
| 21 | `proposal-try-enhancements.md` | `Try` 增强（Catch/Finally 中 Await、块级声明） |
| 22 | `proposal-using-synclock-enhancements.md` | `Using`/`SyncLock` 增强 |
| 23 | `proposal-postfix-casting.md` | 后置转换语法（`(As Type)`） |
| 24 | `proposal-return-byref.md` | `Return` 赋值给 `ByRef`/`Out` 参数 |

### 4. UI / XML / XAML
| # | 文件名 | 建议 |
|---|--------|------|
| 25 | `proposal-xaml-literals.md` | XAML 字面量 |
| 26 | `proposal-embedded-vb-mode.md` | 嵌入 VB 解析模式（`<?vb?>`） |
| 27 | `proposal-xml-schema-types.md` | XML Schema 类型 |

### 5. JSON 与 JSON 模式匹配
| # | 文件名 | 建议 |
|---|--------|------|
| 28 | `proposal-json-literals.md` | JSON 字面量与目标类型创建 |
| 29 | `proposal-json-pattern-matching.md` | JSON 模式匹配 |

### 6. 字符串与字符串模式匹配
| # | 文件名 | 建议 |
|---|--------|------|
| 30 | `proposal-string-pattern-matching.md` | 字符串模式匹配（插值逆运算） |
| 31 | `proposal-interpolated-string-optimization.md` | 插值字符串优化与 `StringBuilder &=` |
| 32 | `proposal-string-narrowing-conversions.md` | 字符串窄化转换（Parse/TryParse/Enum） |

### 7. 通用模式匹配
| # | 文件名 | 建议 |
|---|--------|------|
| 33 | `proposal-shapeof-pattern-matching.md` | `ShapeOf` 模式匹配 |
| 34 | `proposal-user-defined-pattern-methods.md` | 用户定义模式方法 |
| 35 | `proposal-named-patterns.md` | 命名模式（解构语法） |

### 8. 声明现代化
| # | 文件名 | 建议 |
|---|--------|------|
| 36 | `proposal-out-arguments.md` | `Out` 实参/形参 |
| 37 | `proposal-module-enhancements.md` | 模块增强（泛型/嵌套/`StandardModule`） |
| 38 | `proposal-method-level-imports.md` | 方法级 `Imports` |
| 39 | `proposal-bit-enum.md` | `Bit Enum` 位枚举 |
| 40 | `proposal-delegate-enhancements.md` | 委托增强与匿名委托类型 |
| 41 | `proposal-pipeline-operator.md` | 管道运算符 `->` |

### 9. LINQ 增强
| # | 文件名 | 建议 |
|---|--------|------|
| 42 | `proposal-range-expressions.md` | 范围表达式（`1 To 10 Step 2`） |
| 43 | `proposal-query-enhancements.md` | 查询增强（元组解构、聚合函数、目标类型化 `Select`、BC36606 修复） |
| 44 | `proposal-query-comprehensions.md` | 新查询理解（`Include` 关联加载；Skip/Take Until、Left/Right Join 待定） |
| 45 | `proposal-insert-update-delete-expressions.md` | `Insert`/`Update`/`Delete` 表达式 |
| 46 | `proposal-initializer-enhancements.md` | 初始化器增强（嵌套、`With`+`From` 组合） |
| 47 | `proposal-in-notin-operators.md` | `In`/`NotIn` 运算符（映射 `Contains`） |

### 10. 动态编程增强
| # | 文件名 | 建议 |
|---|--------|------|
| 48 | `proposal-any-pseudotype.md` | `Any` 伪类型 |
| 49 | `proposal-typeless-declarations.md` | 无类型声明的默认类型 |
| 50 | `proposal-default-methods.md` | `Default` 方法（可调用对象）与晚期绑定增强 |

### 11. 异步编程增强
| # | 文件名 | 建议 |
|---|--------|------|
| 51 | `proposal-async-sub.md` | `Async Sub` 返回类型与默认异步类型配置 |
| 52 | `proposal-agile-async.md` | `Agile Async` 与 `Await` 省略 |
| 53 | `proposal-require-await-call.md` | 强制 Await 与 `Call` 语句 |
| 54 | `proposal-async-iterator.md` | `Await Each` 与 `Async Iterator` |
| 55 | `proposal-async-event.md` | `Async Event` |

### 12. Null 与 Nothing
| # | 文件名 | 建议 |
|---|--------|------|
| 56 | `proposal-null-literal.md` | `Null` 字面量与可空推断 |
| 57 | `proposal-null-equality-operators.md` | 二值逻辑相等运算符（`?=`、`?<>`） |
| 58 | `proposal-null-coalescing.md` | 后置空合并运算符与空指示符 `?` |
| 59 | `proposal-do-nothing.md` | `Do Nothing` 语句 |
| 60 | `proposal-null-safe-behaviors.md` | 空安全行为（`?.` 在语句中的应用） |

### 13. 声明式编程与代码生成
| # | 文件名 | 建议 |
|---|--------|------|
| 61 | `proposal-smart-attributes.md` | 智能属性（`PropertyHandlerAttribute`） |
| 62 | `proposal-replacement-modifiers.md` | `Replaceable`/`Replaces`/`MustReplace` 修饰符 |
| 63 | `proposal-partial-members.md` | `Partial` 成员 |
| 64 | `proposal-semantic-preprocessing.md` | 语义预处理（`##If TYPE_EXISTS/MEMBER_EXISTS`） |

### 14. 类型系统增强
| # | 文件名 | 建议 |
|---|--------|------|
| 65 | `proposal-intersection-union-types.md` | 交集 / 并集类型 |
| 66 | `proposal-array-pseudotype.md` | 数组伪类型 `Array(Of T)` |
| 67 | `proposal-date-time-literals.md` | 日期/时间字面量增强 |

### 15. 继承、接口与扩展
| # | 文件名 | 建议 |
|---|--------|------|
| 68 | `proposal-override-signature-relaxation.md` | 覆写签名放宽 |
| 69 | `proposal-implicit-interface-implementation.md` | 隐式接口实现 |
| 70 | `proposal-interface-delegation.md` | 接口实现委托给字段/属性 |
| 71 | `proposal-extension-properties.md` | 扩展属性 |

### 16. 性能与互操作
| # | 文件名 | 建议 |
|---|--------|------|
| 72 | `proposal-name-resolution.md` | 名称解析优化（`Console` 命名空间遮蔽修复） |
| 73 | `proposal-initonly-mustinit.md` | `InitOnly`/`MustInit` 属性 |

### 17. 运行时库（非语法，仅记录）
| # | 文件名 | 建议 |
|---|--------|------|
| 74 | `proposal-runtime-library.md` | VB 运行时库改进（性能/异常/晚期绑定支持） |

### 18. 实验性想法（`inactive/`）
| # | 文件名 | 建议 |
|---|--------|------|
| 75 | `inactive/proposal-target-typed-conversions.md` | 目标类型化转换 |
| 76 | `inactive/proposal-guarded-let.md` | Guarded `Let`（`Let ... Then/Else/End Let`） |
| 77 | `inactive/proposal-case-else-variable.md` | `Case Else` 变量 |
| 78 | `inactive/proposal-exclusive-for-upper-bound.md` | 独占上界 `To <` |
| 79 | `inactive/proposal-parallel-extensions.md` | 并行扩展 |
| 80 | `inactive/proposal-retry-resume.md` | `Retry`/`Resume` |
| 81 | `inactive/proposal-robust-mapping.md` | 更健壮的映射 |
| 82 | `inactive/proposal-json-serializers.md` | VB 惯用 JSON 序列化器 |
| 83 | `inactive/proposal-case-insensitivity.md` | 大小写不敏感/排序规则集成 |
| 84 | `inactive/proposal-string-pattern-lookahead.md` | 字符串模式前瞻/回溯 |
| 85 | `inactive/proposal-string-span-utf8.md` | `String` 与 `Span`/UTF-8 |
| 86 | `inactive/proposal-named-pattern-inputs.md` | 命名模式输入 |
| 87 | `inactive/proposal-patterns-as-data.md` | 模式作为数据 |
| 88 | `inactive/proposal-plinq-async-queries.md` | PLINQ / 异步查询 |
| 89 | `inactive/proposal-configureawait-options.md` | `ConfigureAwait` 选项与未观测异常 |
| 90 | `inactive/proposal-nullable-reference-types.md` | 可空引用类型 |
| 91 | `inactive/proposal-duplicate-declarations.md` | 重复声明处理 / `Appendable` |
| 92 | `inactive/proposal-generative-compiler-scripting.md` | 生成式编译器脚本 |
| 93 | `inactive/proposal-units-of-measure.md` | 度量单位 |
| 94 | `inactive/proposal-annotated-types.md` | 注释类型 / 类型变体 |
| 95 | `inactive/proposal-case-classes.md` | `Case` 类/结构/接口 |
| 96 | `inactive/proposal-structure-constraint-overloading.md` | `Structure` 约束重载 |
| 97 | `inactive/proposal-native-instruction-helpers.md` | 平台原生指令辅助 |
| 98 | `inactive/proposal-scripting-interpreted.md` | 脚本与解释执行 |
| 99 | `inactive/proposal-chained-ternary.md` | 链式三元表达式 |
| 100 | `inactive/proposal-type-predicates.md` | 类型谓词（流敏感类型） |
| 101 | `inactive/proposal-versioning-tools.md` | 版本化工具 / `Imports Wpf` |
| 102 | `inactive/proposal-rust-ownership.md` | 任意块作用域、Rust 式所有权、AI（18.28–18.30 合并） |

---

## 后续步骤

1. 用户审核上述建议清单与各 proposal 文档。
2. 定稿建议后，补充 LDM 会议记录（`../meetings/`）。
3. 最终形成语言规范（`../spec/`）。
