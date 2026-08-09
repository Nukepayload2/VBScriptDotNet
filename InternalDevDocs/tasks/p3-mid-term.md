# P3 中期实现（ModVB 语法）

## 头部

- **判入依据**：其余 **Consider**（中低价值）+ **Table 有复活信号**（窄种子 / source-gen 契约 / 家族阶段 2–3）+ inactive 有信号项。实施须先满足各条目标注的**复活信号**。
- **提案路径**：相对 `modvb\`；会议纪要 = `meetings\meeting-<slug>.md`（inactive 在 `meetings\inactive\`）。
- **复活信号**：各会议「三态判定」给的 Active/Consider 拆分项、C# 先例、家族文法落地顺序；信号未到不启动。

---

## 一、模式家族阶段 2–3

| 提案 | 路径 | 要点 | 三态 | 复活信号 |
|---|---|---|---|---|
| 用户定义模式方法（34） | `proposals\inactive\proposal-user-defined-pattern-methods.md` | 带 Out 参数返回 Boolean 的函数作模式，判定+取值一次完成 | Table（归家族 Phase 2 Active） | **前置闸门：家族统一文法 + 36 Out 实参落地**（Phase 1 之后） |
| 命名模式（35） | `proposals\inactive\proposal-named-patterns.md` | 函数即模式，以解构语法递归匹配对象形状 | Table（概念 Active） | 家族 Phase 3；工厂镜像与构造语法协调 |
| Select Case 增强（15） | `proposals\inactive\proposal-select-case-enhancements.md` | 类型/形状/恒等/Like/集合成员匹配，并入模式家族 | Table | 家族统一文法后并入（33 Phase 1 之后） |

## 二、字符串 / JSON / 数据形态

| 提案 | 路径 | 要点 | 三态 | 复活信号 |
|---|---|---|---|---|
| JSON 模式匹配（29） | `proposals\inactive\proposal-json-pattern-matching.md` | 用 JSON 形状做模式匹配断言与分发，配 JSON Schema 标注 | Table（形状模式 Consider） | 家族统一文法；28 JSON 字面量落地后 |
| 字符串模式匹配（30） | `proposals\inactive\proposal-string-pattern-matching.md` | 插值字符串当模式，`{message}` 占位符抽取片段绑定变量 | Table（B 子集 Consider） | 家族统一文法；B 子集（零破坏子集）先评估；插值逆运算破坏风险最重 |
| 插值字符串优化（31） | `proposals\inactive\proposal-interpolated-string-optimization.md` | 合并相邻、常量孔折叠、StringBuilder `&=` | Table（B→Active） | 优化而非特性；B 项（`&=`）考虑转 Active |
| 字符串窄化转换（32） | `proposals\inactive\proposal-string-narrowing-conversions.md` | Parse 常量、TryParse 配 ShapeOf、转枚举 | Table（枚举窄化 Consider） | 枚举窄化（值类型）先行；配 36 Out |
| XML Schema 类型（27） | `proposals\inactive\proposal-xml-schema-types.md` | 语言级 `<geo:Address>` 类型标注恢复 XML IntelliSense | Table（注释变体 Consider） | 注释型变体（不经编译器语义）优先 |
| 数组伪类型（66） | `proposals\inactive\proposal-array-pseudotype.md` | `Array(Of T)` 拼写与 `New Array(Of T)(size)` 按大小实例化 | Table（按大小实例化 Consider） | 按大小实例化（newarr）先于拼写 |

## 三、source-gen 契约（声明式/生成器）

| 提案 | 路径 | 要点 | 三态 | 复活信号 |
|---|---|---|---|---|
| XAML 字面量（25） | `proposals\inactive\proposal-xaml-literals.md` | `<?xaml?>` 内联 XAML 声明，编译器降级为 UI 对象树 | Table（source-gen 契约 Consider） | 走 source-gen（A 附复活信号）；「Fantastic idea, and too hard to do」判例 |
| 嵌入 VB 模式（26） | `proposals\inactive\proposal-embedded-vb-mode.md` | `<?vb?>` 开启 VB 解析模式，HTML 即 XML 字面量 | Table（C source-gen Consider） | C 方案（source-gen 模板）先行 |
| 智能属性（61） | `proposals\inactive\proposal-smart-attributes.md` | 可组合特性注入属性/事件访问器行为，免源生成器 | Table | 收敛为内置处理器集（不开放可编程扩展） |
| 替换修饰符（62） | `proposals\inactive\proposal-replacement-modifiers.md` | Replaceable/Replaces/MustReplace 声明-替换协作契约 | Table（MustReplace Consider） | 只取 MustReplace 种子；配合 ##If（64） |
| 重复声明/Appendable（91） | `proposals\inactive\proposal-duplicate-declarations.md` | 源生成器版多播委托处理重复定义/名称冲突 | Table | 配合 64 ##If / source-gen 生态 |

## 四、Table 窄种子（有 Active/Consider 拆分项）

| 提案 | 路径 | 要点 | 三态 | 复活信号 |
|---|---|---|---|---|
| 空安全行为（60） | `proposals\rejected\proposal-null-safe-behaviors.md` | `?.` 扩展到语句层整体跳过 | Reject（两窄子集 Consider） | 析出**空条件赋值 / 空条件 Await** 两窄子集（后者配 21） |
| 空合并（58） | `proposals\inactive\proposal-null-coalescing.md` | 后置 `??` 与空指示符 `?` | Table（True? Consider） | 只取 `True?` 常量空指示符；`??` 与 VB 既有 `If(a,b)` 重复 Reject |
| Bit Enum（39） | `proposals\inactive\proposal-bit-enum.md` | 位枚举专用语法：自动 2 的幂、逗号掩码、位段 | Table（声明侧 Consider） | 声明侧（自动 2 的幂）优先，掩码/位段挂起 |
| Throw 推断（20） | `proposals\inactive\proposal-throw-inference.md` | 裸 Throw 依条件形态推断异常类型 | Table（`Is Nothing→ArgumentNullException` 窄种子 Table） | 窄种子只做参数校验惯用法；无广泛形态推断 |
| Using/SyncLock 增强（22） | `proposals\inactive\proposal-using-synclock-enhancements.md` | 块内 Catch/Finally、头解构、按名 Dispose | Table（解构头 Active） | 只取解构头（`Using (x, y)`）；配 13 Let 解构 |
| 初始化器增强其余（46 余项） | `proposals\proposal-initializer-enhancements.md` | 嵌套/With+From 组合的其余部分 | Consider | C 项已入 P2，其余按需 |
| NRT（90） | `proposals\inactive\proposal-nullable-reference-types.md` | 为 VB 重设计 NRT，反转默认标记「保证非空」 | Table（切片1 Active） | 切片1 流分析 Active（与 02 轨道1 合并）；需前置-2 M8 元数据识别 |

## 五、其余 Consider（中低价值 / 架构影响）

| 提案 | 路径 | 要点 | 三态 | 说明 |
|---|---|---|---|---|
| 模块增强（37） | `proposals\inactive\proposal-module-enhancements.md` | 模块可泛型/嵌套，默认不提升成员 | Table（泛型非提升 Consider） | 泛型非提升窄子集（Consider，返工后可 Active），其余 StandardModule 回退 |
| 方法级 Imports（38） | `proposals\proposal-method-level-imports.md` | 方法级 Imports 收窄命名空间/Shared 成员导入作用域 | Consider | 低风险作用域收窄 |
| Default 方法（50） | `proposals\proposal-default-methods.md` | Default 方法使对象可像函数调用 | Consider | 可调用对象 v1（M7）；依赖 D1 ref struct 方向 |
| 范围表达式（42） | `proposals\inactive\proposal-range-expressions.md` | `1 To 10 Step 2` 作 For Each 迭代源 | Table（仅迭代源 Consider） | v1 只做 For Each/From 迭代源子集 |
| 查询增强（43） | `proposals\proposal-query-enhancements.md` | From 元组解构、Select 目标类型化、BC36606 修复 | Consider | 守「纯语法重写、不识别 provider 语义」纪律 |
| Do 循环头（18） | `proposals\proposal-do-enhancements.md` | Do 循环头先赋值再测条件 | Consider | 消除重复读取样板 |
| 接口委托（70） | `proposals\proposal-interface-delegation.md` | 接口实现委托给字段/属性，字段 Implements 即转发 | Consider | 组合优于继承；M7 |
| 覆写签名放宽（68） | `proposals\proposal-override-signature-relaxation.md` | 覆写返回类型协变为派生类型 | Consider（范围收缩挂起） | 范围收缩未定，挂起 |
| 顶级代码全家桶（05 余项） | `proposals\proposal-top-level-code.md` | `.vbxhtml`/notebook 沉浸式文件 | Consider（窄子集已入 P1） | 窄子集落地方可评估全家桶 |
| Async Iterator 构建端（54 余项） | `proposals\proposal-async-iterator.md` | Async Iterator / Yield 构建 IAsyncEnumerable | Consider（消费端已入 P2） | 消费端落地方可评估构建端 |
| 运行时库（74） | `proposals\inactive\proposal-runtime-library.md` | 转换性能、异常质量、晚期绑定、My 八条改进 | Table（性能/二进制 Active） | 伞形拆分：性能 Active、异常 Consider、晚期绑定 Table；M5 支撑 |
| 目标类型化转换（75） | `proposals\inactive\proposal-target-typed-conversions.md` | 转换表达式省略类型实参由目标类型推断 | Table | 配 23 后置转换家族；DirectCast/TryCast 目标类型化 |

## 六、inactive 有信号项（章 18）

| 提案 | 路径 | 要点 | 三态 | 复活信号 |
|---|---|---|---|---|
| 类型谓词（100） | `proposals\inactive\proposal-type-predicates.md` | 任意谓词即命名类型进流敏感类型 | Table（谓词型接口 Reject） | 值类型精化（流敏感）先行；谓词型接口 Reject |
| native-instruction-helpers（97） | `proposals\inactive\proposal-native-instruction-helpers.md` | 扩展内联列表 + well-known 模块，IL 经 VB 表达 | Table | M1；VB 安全 IL 出口，需 unsafe 元数据桥（前置-2 M8） |
| units-of-measure（93） | `proposals\inactive\proposal-units-of-measure.md` | F# 式度量单位 | Table | M6；唯一无争议结论是泛型运算符；CLR 无天然表达需编译器合成 |
| string-span-utf8（85） | `proposals\inactive\proposal-string-span-utf8.md` | String 对 Span/UTF-8 优雅处理 | Table | M6；与 C# `u8`/Span 同向（D1 后可能转正） |
| 可空引用类型 NRT（90） | `proposals\inactive\proposal-nullable-reference-types.md` | 反转默认「保证非空」的 NRT 重设计 | Table（切片1 Active） | 见「四、Table 窄种子」 |

---

## 七、P3 排除说明
- 本档全部条目**均有明确复活信号或已拆分的 Active/Consider 子项**；信号未到不启动。
- 模式家族 2–3（34/35/15/29/30）统一受**家族统一文法**闸门约束，与 P2 的 33 同一条链条，分批推进。
- inactive 中未列者（76–102 其余）无语法或信号弱，入 P4 观察池。
