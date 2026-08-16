# P1 立即实现（ModVB 语法）

## 头部

- **判入依据**：`..\decisions.md` D4 两档直接判入（① C# 已照顾的非底层内存用例 ② C# interop 用例）+ 三态 **Active**（或拆分 Active 的那一项）。P1 硬约束 = 不引起无谓 regression。
- **提案路径**：相对 `modvb\`；会议纪要 = `meetings\meeting-<slug>.md`（inactive 在 `meetings\inactive\`）。
- **实施前**：回读对应会议「三态判定」复活信号 +「附录：C# 生态与互操作考量」。

---

## 零、基础设施前置（非语法，解锁语法，P1 interop）

### 前置-1. D1：移植 RefStructHelper + 编译器层 suppress ref struct obsolete error
- **来源**：`..\decisions.md` D1 / M7。
- **内容**：把 `{{VBRefStructHelper}}` 的 BCX 系列错误码（BCX31394/31396/32061/36598/36640/37052/31393）移植进编译器内部，并在编译器层面 suppress ref struct obsolete error。
- **D4 依据**：C# interop 用例（consume C#13 `allows ref struct` 接口类型）。P1 硬约束适用：obsolete 类误用 ref struct 由运行期 `InvalidProgramException` 改为正确编译报错 = **修错不算回归**。
- **REPL/脚本侧语义契约**：`..\meetings\meeting-byref-like-repl-safety.md`（2026-08-12）——byref-like 提交内可用、不跨提交持久化；顶层 byref-like `Dim`（脚本类字段）、byref-like 结果（`?`/末尾表达式装箱到 Object）、跨顶层 `Await` 均编译错误；方法体局部/参数/返回值/`allows ref struct` 接口消费可用。D1 落地时按此补 REPL/脚本顶层拒绝与文案（错误码走 restricted-type 族 BC31393/31394/31396/…/37052）。
- **产品提案**：`..\proposals\proposal-byref-like-safety.md`（2026-08-12，Active/Proposed）——byref-like 类型安全的产品化，**对 vbx 与常规编译模式都生效**；D1 实施按该提案 Detailed design（识别 IsRefLikeType + suppress obsolete + 移植 BCX 规则 + 模式特化 4a/4b）。
- **规范说明**：`..\spec\spec-byref-like-safety.md`（2026-08-12）——byref-like 类型安全的能力规范与结构事实；实施细节仍以本条目为准。
- **设计任务**：`byref-like-safety\`（2026-08-12）——概要设计（`design-overview.md`）、详细设计（`design-detailed.md`，改动清单/判定细化/suppress obsolete/REPL 三碰撞点落点/错误码核实）、测试计划（`test-plan.md`，L1-L4 分层矩阵）；实施阶段按其拆 Vortex 代办。
- **解锁**：M7 方向全部（委托增强/接口委托/隐式接口实现消费 ref struct 接口）。

### 前置-2. M8：识别 C# 新元数据属性
- **来源**：`..\decisions.md` M8。
- **内容**：编译器认识 `NullableAttribute` / `OverloadResolutionPriority` / `RefSafetyRules` / `RequiresUnsafeAttribute` / `MemorySafetyRulesAttribute`，以正确校验「调用 C# requires-unsafe 成员 / 读可空性 / 读优先级」。
- **D4 依据**：C# interop 用例（unsafe-evolution 后成员会标 requires-unsafe，VB 无 unsafe 上下文但必须能安全校验调用）。
- **解锁**：02 轨道2（NRT 元数据）、63 Partial、90 NRT、跨语言调用校验。

---

## 一、语法项

### 1. TypeOf 流分析（01）
- **路径**：`proposals\proposal-typeof-flow-analysis.md` ↔ `meetings\meeting-typeof-flow-analysis.md`
- **要点**：`TypeOf ... Is/IsNot` 流敏感类型收窄，免强转直接访问收窄类型成员；与 Select Case TypeOf / ShapeOf 共享流分析引擎。
- **三态**：**Active**（限定范围）。
- **D4 依据**：C# 类型模式（`is T t`）收窄同向，非底层内存机制。零新语法、编译期收窄，无回归风险。
- **落地**：限定范围（不做整引擎重构），作流分析引擎首块地基，被 02/33 复用。

### 2. 可空性流分析（02，轨道1）
- **路径**：`proposals\proposal-nullability-flow-analysis.md` ↔ `meetings\meeting-nullability-flow-analysis.md`
- **要点**：可空类型空状态流分析，`IsNot Nothing` 守卫后视为非空。轨道1 = 可空值类型；轨道2 = 可空引用类型（Table，入 P3）。
- **三态**：**Active(轨道1)** / Table(轨道2)。
- **D4 依据**：NRT 元数据互操作（M8）+ C# null-state analysis 同向。
- **落地**：先轨道1；轨道2 依赖前置-2 元数据识别后于 P3 评估。

### 3. 顶级代码（05，窄子集）
- **路径**：`proposals\proposal-top-level-code.md` ↔ `meetings\meeting-top-level-code.md`
- **要点**：沉浸式文件——Module/Class 之外直接写语句，整文件即程序（脚本化身份核心）。
- **三态**：**Consider（窄子集候选 Active）**。
- **D4 依据**：C#9 top-level statements 同向（① 非底层内存用例），且是 VBScript.NET 脚本化身份的地基。
- **落地**：**只取窄子集**（直接写语句 = 顶层程序），`.vbxhtml`/notebook 全家桶留 P3/P4。与 98 脚本化记录同源，先定 M5 双模路线。

### 4. 简写属性/事件（08，Return 表达式体）
- **路径**：`proposals\proposal-abbreviated-properties-events.md` ↔ `meetings\meeting-abbreviated-properties-events.md`
- **要点**：压缩属性/事件声明样板，五合一；本次只取 `Return` 表达式体。
- **三态**：**Active（仅 Return 表达式体）**。
- **D4 依据**：C# expression-bodied members 同向，样板消除，零破坏。
- **落地**：仅 `Return` 表达式体；`Set(value)`/End 省略等其余部分留 P3。

### 5. 杂项修复 1/2/4/7（12）
- **路径**：`proposals\proposal-minor-fixes.md` ↔ `meetings\meeting-minor-fixes.md`
- **要点**：七项小修复打包，逐项裁决；1/2/4/7 Active（Async Main、Optional 默认值推断、NameOf 等）。
- **三态**：**拆分(1/2/4/7 Active)**。
- **D4 依据**：与 C# 对齐（Async Main 等），低风险小项。
- **落地**：只实施 Active 的 1/2/4/7，其余项不入 P1。

### 6. Try 增强（21，Await in Catch/Finally）
- **路径**：`proposals\proposal-try-enhancements.md` ↔ `meetings\meeting-try-enhancements.md`
- **要点**：Try/Catch/Finally 块内可 Await（异步清理/重试），头块级声明。
- **三态**：**Active**（Await in Catch/Finally）。
- **D4 依据**：C# async + try 组合同向；「Treat it as an optimization, not a feature」判例，非破坏。
- **落地**：Await in Catch/Finally；Try 头声明等其余留 P3。

### 7. Return ByRef/Out 写回（24）
- **路径**：`proposals\proposal-return-byref.md` ↔ `meetings\meeting-return-byref.md`
- **要点**：`Return True, value:=result` 一次返回函数值同时写回 ByRef/Out 输出参数。
- **三态**：**Active**。
- **D4 依据**：C# ref returns（C#7+）同向，M3 byref 互操作。
- **落地**：注意 VB `ByRef`≠C# `ref`（VB 不参与 ref-safe-context），需明示逃逸语义避免被 C# ref 安全规则卡住（M3 考量）。

### 8. Out 实参（36）
- **路径**：`proposals\proposal-out-arguments.md` ↔ `meetings\meeting-out-arguments.md`
- **要点**：调用点隐式声明 Out 实参（`TryGetValue(x, out y)`），只写 Out 形参；Try*/模式方法地基。
- **三态**：**Active**。
- **D4 依据**：C# `out` 同向（M3），非破坏。
- **落地**：与 24 成对推进；是用户定义模式方法（34，P3）的前提。

### 9. 日期/时间字面量（67，仅毫秒）
- **路径**：`proposals\proposal-date-time-literals.md` ↔ `meetings\meeting-date-time-literals.md`
- **要点**：`#...#` 日期字面量支持毫秒 / DateTimeKind，DateOnly/TimeOnly 后缀。
- **三态**：**Active（仅毫秒）**。
- **D4 依据**：C# 对齐，低风险纯增量。
- **落地**：仅毫秒部分；Kind/后缀留 P3。

### 10. 隐式接口实现（69，协变返回）
- **路径**：`proposals\proposal-implicit-interface-implementation.md` ↔ `meetings\meeting-implicit-interface-implementation.md`
- **要点**：同名同签名公共成员自动满足接口实现；本次只取返回类型协变放宽。
- **三态**：**部分采纳（协变返回 Active）**。
- **D4 依据**：C# covariant returns 同向（M7）。
- **落地**：仅协变返回；裸隐式实现 Reject 不入。

### 11. InitOnly/MustInit（73）
- **路径**：`proposals\proposal-initonly-mustinit.md` ↔ `meetings\meeting-initonly-mustinit.md`
- **要点**：属性修饰符：InitOnly 仅初始化期可写、MustInit 必填编译期强制。
- **三态**：**Consider**。
- **D4 依据**：**直接判入 P1（① C# 已照顾）**——对齐 C# `init`/`required`（M8），与 C# 生态同向、风险最低。
- **落地**：命名按 VB 语义（InitOnly=init、MustInit=required），识别 C# 对应元数据属性以互操作。

### 12. `.vbx` 首行 `#!` shebang 指令（产品原生提案，D4 ①）
- **路径**：`..\proposals\proposal-shebang-directive.md` ↔ `..\meetings\meeting-shebang-directive.md`；设计任务 `shebang-directive\`（`../tasks/shebang-directive/`）
- **要点**：脚本文件首行 `#!` shebang（如 `#!/opt/vbi-n2fork/vbi`）作**编译器指令 trivia**——仅 script 模式（`IsScript`）、位置 0（首字符、BOM 不能在前）、`#!` 后整行吞为 trivia、error severity、`Content`/`WithContent` API；支持 Linux/macOS 直接执行 `.vbx`，诊断行号不漂移。
- **三态**：**Active**。
- **D4 依据**：**直接判入 P1（① C# 已照顾）**——C# 14 `ignored-directives`（champion #8617，`#!`/`#:` ignored 指令）同向，非底层内存机制。
- **状态**：**已实现**——M0（Syntax 节点 + 再生成 + Content API）、M1（派发 + `ParseShebangDirective` + 错误码 37003/37004）、M2（四层测试全绿）完成；能力规范 `..\spec\spec-shebang-directive.md`。

---

## 二、P1 排除说明
- 01/02/08/12/21/24/36/67/69 为三态 Active（或拆分 Active 项），73 与 05 为 D4 判入；其余提案一律不在 P1。
- 模式家族（33/34/35）、Any/后置转换（48/23）、JSON 字面量（28）等**差异化面**不满足 D4「C# 已照顾/互操作」，按 LDM 归 P2/P3。
