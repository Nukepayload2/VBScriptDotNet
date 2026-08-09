# P4 观察池（ModVB 语法）

## 头部

- **判入依据**：**Table 无活跃子项** / **Reject 已裁定** / **inactive 章18 实验面（信号弱、多数无语法）**。
- **提案路径**：相对 `modvb\`；会议纪要 = `meetings\meeting-<slug>.md`（inactive 在 `meetings\inactive\`）。
- **注意**：本档是**观察/留档**池，不是「永不做」——个别条目有 Consider 子项，复活信号一到可提级评估（见各条目备注）。Reject 条目除非 C#/CLR 现实反转，不再重开。

---

## 一、Reject 已裁定（留档，不实现）

| 提案 | 路径 | 要点 | 裁定 | 备注 |
|---|---|---|---|---|
| 管道运算符（41） | `proposals\rejected\proposal-pipeline-operator.md` | `->` 改写嵌套调用为从左到右数据流 | Reject（窄方案 Consider） | 仅保留窄方案观察（It 记号）；与 VB 根基冲突 |
| Async Sub 签名（51） | `proposals\rejected\proposal-async-sub.md` | Async Sub 声明 Task/ValueTask 返回类型 | Reject | 签名是源码投影非配置投影；Async Sub 默认 void/fire-and-forget 不可重载 |
| Require Await（53） | `proposals\rejected\proposal-require-await-call.md` | 异步调用必须 Await 或赋给任务对象 | Reject(语言)/Active(分析器) | 语言强制 Reject；**官方分析器 Active**（与 C# CS4014 同构）→ 分析器任务另行跟踪 |
| Async Event（55） | `proposals\rejected\proposal-async-event.md` | 声明异步事件，handler 可异步执行 | Reject | 库方案（AsyncEventHandler）覆盖 |
| Null-safe 捆绑（60） | `proposals\rejected\proposal-null-safe-behaviors.md` | `?.` 扩展到语句层整体跳过 | Reject（两窄子集 Consider） | 窄子集（空条件赋值/Await）已析出入 **P3** |

## 二、Table 无活跃子项（观察）

| 提案 | 路径 | 要点 | 三态 | 复活信号 |
|---|---|---|---|---|
| 通配符 Lambda（07） | `proposals\proposal-wildcard-lambdas.md` | `*.Member` 取代单参纯成员访问 lambda | Consider（根目录 active） | 依赖表达式树/EF 配置生态；P4 优先级但文件夹归 active |
| Markdown 文档注释（09） | `proposals\inactive\proposal-markdown-doc-comments.md` | 文档注释改 Markdown 小节/@引用/围栏 | Table/Consider | XML doc 兼容性顾虑；需 IDE 生态配合 |
| `#Ignore Warning`（10） | `proposals\inactive\proposal-ignore-warning-directive.md` | 单行按错误号关段内警告 | Table | 与 C# pragma 对齐度低；低收益 |
| Set 赋值语句（14） | `proposals\inactive\proposal-set-statement.md` | 显式 Set 赋值，复合/多重/解构赋值 | Table（解构赋值 Consider） | 解构赋值子项可随 **13 Let 解构**落地后提级评估 |
| 查询理解 Include（44） | `proposals\inactive\proposal-query-comprehensions.md` | For Each 内 Include 关联加载理解 | Table | 语言关键字不值得；配 source-gen 生态评估 |
| Insert/Update/Delete 表达式（45） | `proposals\inactive\proposal-insert-update-delete-expressions.md` | 声明式数据源增删改 | Table | 表达式副作用与 VB 根基冲突 |
| In/NotIn 运算符（47） | `proposals\inactive\proposal-in-notin-operators.md` | In/NotIn 映射 Contains | Table | 区间碎片挂起；与 42 范围表达式关联 |
| 无类型声明（49） | `proposals\inactive\proposal-typeless-declarations.md` | 无类型声明默认用 Any 动态 | Table（收进脚本模式） | 随 **48 Any**（P2）的脚本模式 D 评估 |
| Agile Async（52） | `proposals\inactive\proposal-agile-async.md` | 免写 ConfigureAwait(False)，省略中间 Await | Table | 省略 Await Reject；符号敏感度风险高 |
| Null 字面量（56） | `proposals\inactive\proposal-null-literal.md` | 统一空引用与空值 | Table | 与 VB 基因冲突；依赖未定型 NRT |
| `?=`/`?<>`（57） | `proposals\inactive\proposal-null-equality-operators.md` | 二值逻辑相等，等价 IS DISTINCT FROM | Table | 不引新语法走 EqualityComparer |
| Do Nothing（59） | `proposals\inactive\proposal-do-nothing.md` | 显式空操作语句占位 | Table | 安全但不值表面 |

## 三、inactive 章18 其余（信号弱 / 多数无语法）

> 以下全部保持 inactive（Table），多数仅一句观察、无语法无设计；无激活信号不启动。复活需「数据需求 / C# 先例 / 家族文法落地」信号（见各会议三态判定）。

| 提案 | 路径 | 要点 | 备注 |
|---|---|---|---|
| Guarded Let（76） | `proposals\inactive\proposal-guarded-let.md` | Let 声明加 Then/Else 守卫 | 依赖流分析引擎（01/02）成熟后 |
| Case Else 变量（77） | `proposals\inactive\proposal-case-else-variable.md` | Case Else 后跟变量绑定未匹配值 | 归模式家族评估 |
| 独占上界 `To <`（78） | `proposals\inactive\proposal-exclusive-for-upper-bound.md` | 范围表达式左闭右开 | 随 42 迭代源子集 |
| 并行扩展（79） | `proposals\inactive\proposal-parallel-extensions.md` | 并行语言集成 | 现阶段 Parallel.For 已够用 |
| Retry/Resume（80） | `proposals\inactive\proposal-retry-resume.md` | Try 覆盖 On Error 弃用 | 依赖 21 Try 增强落地 |
| Robust Mapping（81） | `proposals\inactive\proposal-robust-mapping.md` | null 键标注不映射成员，ShapeOf 校验 | 随模式家族 |
| JSON 序列化器（82） | `proposals\inactive\proposal-json-serializers.md` | VB 写 JSON 序列化器应有自然抽象 | 随 28 JSON 字面量；守 Utf8JsonReader/source-gen |
| 大小写不敏感（83） | `proposals\inactive\proposal-case-insensitivity.md` | 排序规则与内置字符串比较集成 | Option Compare 语义风险高 |
| 字符串模式前瞻/回溯（84） | `proposals\inactive\proposal-string-pattern-lookahead.md` | 零分配 lazy-greedy 回溯 | 随 30 字符串模式（P3） |
| 命名模式输入（86） | `proposals\inactive\proposal-named-pattern-inputs.md` | 命名模式实参可否承载输入 | 随 35 命名模式（P3） |
| 模式作为数据（87） | `proposals\inactive\proposal-patterns-as-data.md` | 模式成为一等可组合数据 | 概念级；依赖模式家族成熟 |
| PLINQ/异步查询（88） | `proposals\inactive\proposal-plinq-async-queries.md` | 六查询方向登记册 | 随 43 查询增强 |
| ConfigureAwait 选项（89） | `proposals\inactive\proposal-configureawait-options.md` | ConfigureAwait 选项与未观测异常配置 | 随 52 Agile Async 观察；「Void-returning async methods are my white whale」 |
| 生成式编译器脚本（92） | `proposals\inactive\proposal-generative-compiler-scripting.md` | 脚本驱动改写编译过程 | **作者自评坏主意**；M5 可信边界，维持 Reject 倾向 |
| 注释类型（94） | `proposals\inactive\proposal-annotated-types.md` | XML/JSON 类型变体以注释标注 | 随 27/28 注释变体 |
| Case 类（95） | `proposals\inactive\proposal-case-classes.md` | 封闭分支类型族 | 与 C# unions（M4）方向同，随类型系统工作 |
| 结构约束重载（96） | `proposals\inactive\proposal-structure-constraint-overloading.md` | 按 Class/Structure 约束分派泛型方法 | 「Fantastic idea, and too hard to do」判例 |
| 版本化工具（101） | `proposals\inactive\proposal-versioning-tools.md` | 跨命名空间移动/重命名/元命名空间 | TypeForwardedTo 延伸；工具化需求 |
| Rust 所有权（102） | `proposals\inactive\proposal-rust-ownership.md` | 任意块作用域、Rust 式所有权 | **Table（所有权 Reject）**：仅 18.29 语言特性 Reject 为子裁定（18.28 语法 Reject/诊断 Table、18.29 analyzer Table、18.30 无法判定）；保留任意块作用域诊断；「It doesn't earn back its -100 points」判例 |
| 链式三元 `? If()`（99） | `proposals\inactive\proposal-chained-ternary.md` | `? If(条件,值,…,Else,兜底)` | Table（`? If()` Reject） | 只留档；破坏性小但符号预算已耗尽 |
| 字符串 Span/UTF-8（85） | `proposals\inactive\proposal-string-span-utf8.md` | String 对 Span/UTF-8 优雅处理 | **已列 P3**（M6，D1 后可能转正）——此处仅索引 |
| native-instruction-helpers（97） | `proposals\inactive\proposal-native-instruction-helpers.md` | IL 经 VB 安全表达 | **已列 P3**（M1） |
| units-of-measure（93） | `proposals\inactive\proposal-units-of-measure.md` | F# 式度量单位 | **已列 P3**（M6） |
| 类型谓词（100） | `proposals\inactive\proposal-type-predicates.md` | 谓词即命名类型 | **已列 P3**（流敏感值类型精化） |

---

## 四、P4 说明
- 本档全部条目**复活信号未到**；多数仅一句观察、无语法无设计。信号一到按对应「三态判定」提级（一般先到 P3）。
- Reject 条目（第一部分）除非 C#/CLR 现实反转（如 C# 采纳管道/Async Event）不再重开；53 的分析器部分为独立 Active 任务，不随语言实现。
- 第三部分带「已列 P3」标注的条目仅为索引完整性列出，实际归属见 `p3-mid-term.md`。
