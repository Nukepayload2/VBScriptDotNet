# VBScript.NET 语法优先级任务清单（ModVB 来源）

## 头部

- **本文件用途**：为 VBScript.NET（.vbx）编译器实现路线图，对 `modvb` 目录 102 份提案做**优先级排定**，产出 P1–P4 四档任务清单。每档一个 md 文件（`p1-immediate.md` / `p2-short-term.md` / `p3-mid-term.md` / `p4-backlog.md`），本 README 是框架与一览。
- **产品关系**：`modvb` 是 **Anthony 的 ModVB 提案库**（Anthony 改版 VB），与 **VBScript.NET**（复活的 VB REPL + 一部分新语法）是**两个产品**；本清单是 VBScript.NET 对 ModVB 语法提案的**评估/借鉴**，绝不等同。VBScript.NET 只采纳 ModVB 思想的**子集**。
- **依据链**：`InternalDevDocs\README.md`（先读 `*-index.md`）→ `..\modvb-index.md`（102 提案三态判定 + 文件地图）→ `..\decisions.md`（**M1–M8 映射 + D1–D4 决策，VBScript.NET 侧唯一权威**）。
- **使用方式**：实施某提案前，先回读对应会议纪要的「三态判定」复活信号与「附录：C# 生态与互操作考量」；P1/P2 的跨提案依赖（如模式家族统一文法、M5 双模路线）在条目内标注 **前置闸门**。

---

## 一、优先级框架

### 判定闸门（D4，`..\decisions.md` 2026-08-09 用户定案）

1. **P1 两档直接判入**：
   - **① C# 已照顾到的、非底层内存机制相关用例** → P1（与 C# 生态同向，风险最低）；
   - **② C# interop 用例**（如 consume ref struct）→ P1。
2. **其余提案**：按 LDM 风险评估（三态判定 + 复杂度/成本/优先级）定为 P1 或更低。
3. **P1 硬约束**：设计上不引起**无谓** regression / breaking change（不改变合法既有代码的正确语义）。对 C# interop，纠正旧错误用法（如 obsolete 类误用 ref struct → 正确报错）属**修错不算回归**。

### 四档定义

| 档 | 名称 | 判入标准 | 对应三态 |
|----|------|---------|---------|
| **P1** | 立即实现 | D4 两档直接判入 + 三态 **Active**（或拆分 Active 部分） | Active / 部分 Active |
| **P2** | 短期实现 | 高价值 **Consider**（脚本身份核心 / 差异化点 / 模式家族阶段 1）+ **D3 已定型待落地**（postfix-casting）+ Active 拆分的未尽种子 | Consider（高价值） |
| **P3** | 中期实现 | 其余 **Consider**（中低价值）+ **Table 有复活信号**（窄种子 / source-gen 契约 / 家族阶段 2–3）+ 低风险小项 | Consider / Table（带信号） |
| **P4** | 观察池 | **Table 无复活信号** / **Reject 窄种子** / **inactive 28 份**（章 18 实验面） | Table / Reject / inactive |

> 注：档位判定的**第一依据是 D4**（对 C# 对齐 / interop 优先），其次才是三态。个别 Table 提案因 D4 判入 P1/P2（如 InitOnly/MustInit 对齐 C# init/required），个别 Active 提案也可能因整包捆绑下调（看拆分 Active 的那一项）。

---

## 二、一览总表

### P1 立即实现（`p1-immediate.md`）

基础设施前置（非语法，但解锁语法）：
- **D1** RefStructHelper 移植进编译器 + 编译器层 suppress ref struct obsolete error（consume C#13 `allows ref struct` 前提）
- **M8** 新元数据识别：`NullableAttribute` / `OverloadResolutionPriority` / `RefSafetyRules` / `RequiresUnsafe`（C# requires-unsafe 成员校验）

语法项：
| 编号 | 提案 | 三态 | D4 依据 |
|---|---|---|---|
| 01 | TypeOf 流分析 | Active（限定范围） | C# 类型模式收窄同向 |
| 02 | 可空性流分析（轨道1 值类型） | Active | NRT 元数据互操作（M8） |
| 05 | 顶级代码（窄子集） | Consider→候选 Active | C#9 top-level statements 同向 + 脚本身份核心 |
| 08 | 简写属性/事件（Return 表达式体） | Active | C# expression-bodied 同向 |
| 12 | 杂项修复 1/2/4/7（Async Main 等） | 拆分 Active | C# 对齐 |
| 21 | Try 增强（Await in Catch/Finally） | Active | C# 对齐 |
| 24 | Return ByRef/Out 写回 | Active | C# ref returns 同向（M3） |
| 36 | Out 实参 | Active | C# out / Try* 同向（M3） |
| 67 | 日期/时间字面量（毫秒） | Active | C# 对齐，低风险 |
| 69 | 隐式接口实现（协变返回） | 部分采纳 Active | C# 协变返回同向（M7） |
| 73 | InitOnly/MustInit | Consider | C# init/required 已照顾（M8） |

### P2 短期实现（`p2-short-term.md`）— 详见文件，要点：
- **动态/类型化出口成对**：Any 伪类型（48）+ 后置转换 `(As Type)`（23，D3 已定型语义）
- **模式家族阶段 1**：ShapeOf `Case pn As Type`（33）
- **差异化点**：JSON 数据字面量（28）
- **样板消除/声明现代化**：Key 字段自动构造（06）、隐式行继续（11）、Let/As New 数组（13）、For 多计数器（16）、For Each 解构（17）、With `.Me`（19）、委托 `+=`（40）、初始化器增强（46）、Partial 成员（63）、##If 语义预处理（64）、扩展属性 v1（71）
- **类型/推断**：If() 最佳公共类型（03）、递归 Lambda 推断（04）、交集类型限局部变量（65）、名称解析 BC30456（72）
- **脚本运行时面**：Async Iterator 消费端 Await Each（54）、脚本化记录解释执行（98，前置 M5 双模路线）

### P3 中期实现（`p3-mid-term.md`）— 要点：
- 模式家族阶段 2–3：用户定义模式方法（34）、命名模式（35）、Select Case 增强并入家族（15）
- 字符串/JSON 家族：JSON 形状模式（29）、字符串模式 B 子集（30）、插值字符串优化（31）、字符串窄化枚举（32）
- source-gen 契约：XAML 字面量（25）、嵌入 VB 模式（26）、XML Schema 注释变体（27）、智能属性（61）
- Table 窄种子：空安全两窄子集（60 拆分）、Null coalescing `True?`（58）、Bit Enum 声明侧（39）、数组伪类型按大小实例化（66）、Throw 推断窄种子（20）、Using/SyncLock 解构头（22）
- 其余 Consider 中低价值：方法级 Imports（38）、Default 方法（50）、查询增强（43）、Do 循环头（18）、替换修饰符 MustReplace（62）、运行时库拆分（74）、NRT 切片1（90）、模块增强（37，Table 泛型非提升 Consider）、范围表达式迭代源（42，Table 仅迭代源 Consider）
- inactive 有信号：类型谓词（100）、native-instruction-helpers（97）、units-of-measure（93）、string-span-utf8（85）

### P4 观察池（`p4-backlog.md`）
- **Reject**：管道运算符（41）、Async Sub 签名（51）、Require Await 语言强制（53）、Async Event（55）、Null-safe 捆绑（60）
- **Table 无复活信号**：Set 语句（14）、Markdown 注释（09）、#Ignore Warning（10）、查询理解（44）、DML 表达式（45）、In/NotIn（47）、无类型声明（49）、Agile Async（52）、Null 字面量（56）、`?=`（57）、Do Nothing（59）、通配符 Lambda（07）（覆写放宽 68 归 P3，范围收缩挂起）
- **inactive 其余 20 份**：章 18 实验面，多数仅一句观察无语法，标复活信号。

---

## 三、跨提案依赖（先决闸门）

| 闸门 | 说明 | 阻塞 |
|------|------|------|
| **D1 RefStructHelper 移植** | 编译器内 ref-safe + suppress obsolete error | consume ref struct 接口类型（M7 方向全部） |
| **M8 元数据识别** | Nullable/OverloadResolutionPriority/RefSafetyRules/RequiresUnsafe | 02 轨道2、63 Partial、90 NRT、跨语言调用校验 |
| **M5 双模路线** | 默认「编译到受管程序集 + source-gen 桥」，interpreted 为 opt-in 兼容层；NativeAOT 走 D2（生成 vbproj→改版编译器编 dll→SDK PublishAot） | 98 脚本化、48 Any、52/54 异步、runtime-library |
| **模式家族统一文法** | ShapeOf/用户定义模式方法/JSON/字符串模式各会议互相要求对表 | 33/34/35/15/29/30 分批复活 |
| **D3 语义锚定** | `(As Type)` = 显式转换（CType），非 TryCast 非隐式 | 23 落地 |

---

## 四、文件索引

| 文件 | 内容 |
|------|------|
| `p1-immediate.md` | P1 立即实现（含基础设施前置） |
| `p2-short-term.md` | P2 短期实现 |
| `p3-mid-term.md` | P3 中期实现 |
| `p4-backlog.md` | P4 观察池 |

> 本任务清单为**文档产出**，供编译器实现路线图使用；每条目均标注提案/会议路径，实施前以对应 meeting 的复活信号为准。判定规则变更以 `..\decisions.md` D4 为权威。
