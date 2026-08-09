# P2 短期实现（ModVB 语法）

## 头部

- **判入依据**：高价值 **Consider**（脚本身份核心 / 差异化点 / 模式家族阶段 1）+ **D3 已定型待落地**（postfix-casting）+ Active 拆分的未尽种子。未命中 D4 直接判入档，按 LDM 风险与价值排此档。
- **提案路径**：相对 `modvb\`；会议纪要 = `meetings\meeting-<slug>.md`。
- **实施前**：回读对应会议「三态判定」复活信号；标注 **前置闸门** 的条目须先完成闸门。

---

## 一、动态 / 类型化出口（成对，VBScript.NET 差异化核心）

### 1. Any 伪类型（48）
- **路径**：`proposals\proposal-any-pseudotype.md` ↔ `meetings\meeting-any-pseudotype.md`
- **要点**：逐变量显式晚期绑定替代 Object；须正名血缘（VB6 动态类型是 `Variant` 而非 `Any`）。
- **三态**：**Consider**。
- **判定**：VB 最大特色面，但 C# dynamic 已边缘化、与 AOT 张力最大——**不判 P1**，排 P2 首位承载「默认安全、按需动态」路线。
- **前置闸门**：**M5 双模路线**（默认编译到受管程序集 + source-gen 桥，interpreted 为 opt-in 兼容层；NativeAOT 走 D2）。**D3** 语义锚定。
- **落地**：与 23 成对，`(As Any)` 单表达式晚期绑定 + `(As Type)` 类型化出口。

### 2. 后置转换 `(As Type)`（23）
- **路径**：`proposals\inactive\proposal-postfix-casting.md` ↔ `meetings\inactive\meeting-postfix-casting.md`
- **要点**：`expr(As Type)` 后置显式转换，链式转换更可读；把动态/Any 值显式转成强类型，后续 `.Member` 早绑定。
- **三态**：**Table**（LDM 搁置），但 **D3 已定型语义**（= 显式转换 CType，非 TryCast、非 Option Strict Off 隐式）。
- **判定**：已定型待落地，排 P2。为 48 的类型化出口。
- **前置闸门**：D3（已定型，无待办）。
- **落地**：按 CType 语义实现；`As?`（TryCast）变体已拒，不实现。

---

## 二、模式家族阶段 1

### 3. ShapeOf 模式匹配（33）
- **路径**：`proposals\inactive\proposal-shapeof-pattern-matching.md` ↔ `meetings\inactive\meeting-shapeof-pattern-matching.md`
- **要点**：Select Case 按运行时类型分发并绑定强类型变量；无关键字 `Case pn As Type`。
- **三态**：**Table（归家族 Phase 1 Active）**。
- **判定**：VBScript 运行时分发血统（TypeName/IsObject/late binding）的现代化，核心差异化语法面，排 P2（家族阶段 1）。
- **前置闸门**：**模式家族统一文法**（与 34/35/15/29/30 对表，各会议互相要求返工）。
- **落地**：先统一家族文法，再分批复活；复用 01/02 流分析引擎。

---

## 三、差异化数据形态

### 4. JSON 字面量（28）
- **路径**：`proposals\proposal-json-literals.md` ↔ `meetings\meeting-json-literals.md`
- **要点**：`{}` JSON 数据字面量目标类型化创建（JsonObject），`&=` 写给 writer。
- **三态**：**Consider（数据字面量）**。
- **判定**：VB 领先 C# 的差异化点（「JSON is the lingua franca of the cloud」），排 P2。
- **落地**：仅数据字面量（目标类型化）；JSON 模式匹配（29）入 P3 归家族。

---

## 四、样板消除 / 声明现代化

### 5. Key 字段自动构造（06）
- **路径**：`proposals\proposal-key-fields-auto-constructors.md` ↔ `meetings\meeting-key-fields-auto-constructors.md`
- **要点**：`Key` 修饰符：类字段自动构造参数，值对象自动 Equals/GetHashCode。
- **三态**：**Consider**。
- **判定**：样板消除高价值（主线 2014 判词「Primary constructors Rejected for VB」背景下差异化回应）；须与 C# primary constructors 元数据互操作考量对齐（M8）。排 P2。

### 6. 隐式行继续（11）
- **路径**：`proposals\proposal-implicit-line-continuations.md` ↔ `meetings\meeting-implicit-line-continuations.md`
- **要点**：Then/Handles/Implements 前及 `) As` 间新增隐式行继续。
- **三态**：**Consider**。
- **判定**：低风险体验改善（脚本「读起来像命令」），排 P2。

### 7. Let 局部声明（13，As New 数组）
- **路径**：`proposals\proposal-local-declarations.md` ↔ `meetings\meeting-local-declarations.md`
- **要点**：Let 声明关键字 + 元组解构，修复 `As New` 数组/匿名类型。
- **三态**：**Consider（As New 数组 Active）**。
- **判定**：声明现代化；Let 本身 Table，先取 **As New 数组** Active 部分。排 P2（拆分取 Active 项）。

### 8. For 增强（16）
- **路径**：`proposals\proposal-for-enhancements.md` ↔ `meetings\meeting-for-enhancements.md`
- **要点**：For 多计数器、Exit/Continue 指定层、按迭代捕获修复（BC42324）。
- **三态**：**Consider**。
- **判定**：控制流增强中收益较明确者，排 P2。

### 9. For Each 增强（17）
- **路径**：`proposals\proposal-for-each-enhancements.md` ↔ `meetings\meeting-for-each-enhancements.md`
- **要点**：元组解构、命名跳转、Where 过滤、Await Each。
- **三态**：**Consider**。
- **判定**：与 54 异步消费端共享 Await Each，排 P2（取解构/命名跳转；Await Each 随 54）。

### 10. With 增强（19，.Me）
- **路径**：`proposals\proposal-with-enhancements.md` ↔ `meetings\meeting-with-enhancements.md`
- **要点**：With 命名变量、`.Me` 伪成员、复合赋值。
- **三态**：**Consider（.Me Active）**。
- **判定**：取 `.Me` Active 项（`&=` 已 Reject），排 P2。

### 11. 委托增强（40，`+=`/`-=`）
- **路径**：`proposals\proposal-delegate-enhancements.md` ↔ `meetings\meeting-delegate-enhancements.md`
- **要点**：委托 `+=`/`-=` 合并（Delegate.Combine）+ `<Function(...)>` 匿名委托类型。
- **三态**：**Consider（`+=` Active）**。
- **判定**：取 `+=`/`-=` Active 项；M7 方向（消费 ref struct 接口）依赖前置-1 D1。排 P2。

### 12. 初始化器增强（46，C 项）
- **路径**：`proposals\proposal-initializer-enhancements.md` ↔ `meetings\meeting-initializer-enhancements.md`
- **要点**：嵌套初始化器、With+From 组合、`!` 字典访问。
- **三态**：**Consider（C Active）**。
- **判定**：取 C 项（Active），排 P2。

### 13. Partial 成员（63）
- **路径**：`proposals\proposal-partial-members.md` ↔ `meetings\meeting-partial-members.md`
- **要点**：Partial 扩展到任意成员，合并特性/Handles/Implements 子句。
- **三态**：**Consider**。
- **判定**：C# partial 同向（声明式/代码生成受控机制），排 P2；依赖前置-2 M8 元数据识别（与源生成器互操作）。

### 14. 语义预处理 `##If`（64）
- **路径**：`proposals\proposal-semantic-preprocessing.md` ↔ `meetings\meeting-semantic-preprocessing.md`
- **要点**：`##If` 语义预处理，TYPE_EXISTS/MEMBER_EXISTS 谓词条件编译；最小可行形态 = 扩展 `#If`。
- **三态**：**Consider**。
- **判定**：编译期可编程性受控机制（与 C# source-gen 生态同向，M5），排 P2。

### 15. 扩展属性（71，v1 只读）
- **路径**：`proposals\proposal-extension-properties.md` ↔ `meetings\meeting-extension-properties.md`
- **要点**：扩展方法机制推广到属性，`<Extension>` 只读泛型扩展属性。
- **三态**：**Consider（v1 只读）**。
- **判定**：C# extensions（C#14/15）同向，排 P2（仅 v1 只读）。

---

## 五、类型 / 推断 / 名称解析

### 16. If() 最佳公共类型（03）
- **路径**：`proposals\proposal-conditional-best-common-type.md` ↔ `meetings\meeting-conditional-best-common-type.md`
- **要点**：If() 推断最近公共基类型替代 Object，编译期解析公共成员。
- **三态**：**Consider**。
- **判定**：目标类型化采纳，与 C# target-typed 同向，排 P2。

### 17. 递归/互递归 Lambda 推断（04）
- **路径**：`proposals\proposal-recursive-lambda-inference.md` ↔ `meetings\meeting-recursive-lambda-inference.md`
- **要点**：递归/互递归 Lambda 类型推断正确工作。
- **三态**：**Consider**（显式签名自引用 Active、互递归 Table）。
- **判定**：取显式签名自引用 Active 项，排 P2。

### 18. 交集类型（65，限局部变量）
- **路径**：`proposals\proposal-intersection-union-types.md` ↔ `meetings\meeting-intersection-union-types.md`
- **要点**：即席交集 `{A,B}` / 并集 `{A Or B}` 类型，多接口约束写声明处。
- **三态**：**Consider（交集限局部变量）**。
- **判定**：与 C# unions（M4）同向、AOT 压力下类型系统承担更多职责；只取交集限局部变量，排 P2。

### 19. 名称解析 BC30456（72）
- **路径**：`proposals\proposal-name-resolution.md` ↔ `meetings\meeting-name-resolution.md`
- **要点**：Imports 命名空间遮蔽致 BC30456 时延续驱动回退到唯一候选。
- **三态**：**Consider**。
- **判定**：兼容性修复（修错不回归），排 P2。

---

## 六、脚本运行时面

### 20. Async Iterator（54，消费端）
- **路径**：`proposals\proposal-async-iterator.md` ↔ `meetings\meeting-async-iterator.md`
- **要点**：IAsyncEnumerable 对称消费（**Await Each**）与构建（Async Iterator / Yield）。
- **三态**：**Consider（消费端先落地）**。
- **判定**：消费端（Await Each）先行，排 P2；与 17 For Each 共享。构建端（Yield）入 P3。

### 21. 脚本化记录 解释执行（98）
- **路径**：`proposals\proposal-scripting-interpreted.md` ↔ `meetings\meeting-scripting-interpreted.md`
- **要点**：脚本化记录（「Of course.」）；真解释器 Reject，走脚本宿主 + REPL + 顶层代码。
- **三态**：**Consider**（inactive 唯一 Consider）。
- **判定**：**产品核心身份**，但架构影响最大（M5 最大摩擦点），排 P2 但**前置闸门 M5 双模路线 + D2 NativeAOT 桥**必须先定。默认走编译到受管程序集，interpreted 为 opt-in 兼容层。

---

## 七、P2 排除说明
- 48/23 是 D3/M5 承载的动态面；33 是模式家族阶段 1。二者在 P1 已声明「不满足 D4 判入」，故在此档，实施顺序**建议在 P1 基础设施（前置-1/前置-2）与 M5 路线定案之后**。
- 其余 Consider（模块增强 37、方法级 Imports 38、Default 方法 50、范围表达式 42、查询增强 43、Do 循环头 18、接口委托 70）价值相对低或风险高，入 P3。
