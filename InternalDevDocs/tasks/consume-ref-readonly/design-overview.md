# 概要设计：消费 ref readonly 返回（Consume Ref Readonly Returns，D4 → P1）

> 状态：概要设计（F1）。依据链：`../../proposals/proposal-consume-ref-readonly.md`（Active/Proposed）→ `../../meetings/meeting-consume-ref-readonly.md`（RESOLUTION 九条，唯一权威）→ `../../decisions.md`（D1/D4/M3）→ `README.md`（共享源码事实）。
> 本设计吸收 meeting RESOLUTION 九条与 proposal Detailed design §1-§6，为 F2 详细设计提供落点与边界；不涉及实现代码细节。
> 源码事实以任务 README「共享源码事实」为基准（本代理已逐条 Read 复核），引用以 `文件:行号` 给出。编译器部分统一前缀 `Compilers\VisualBasic\Portable\`（下文简写 `VB\`）。

## 1. 背景与目标

**目标（一句话）**：让 VB **消费** C# `ref readonly` 返回（如 `ReadOnlySpan(Of T).Item`、`GetPinnableReference`），读取时自动解引用、写入时按接收方分类保证不写穿只读内存——**不引入任何声明语法**，VB 只消费不声明（D1）。

**现状缺口（实证）**：

1. `"abc".AsSpan().Length` → 正常输出 `3`（普通属性可消费）。
2. `"abc".AsSpan()(0)`（`ReadOnlySpan(Of T).Item`，ref readonly 索引器）→ **BC30643**「Property … is of an unsupported type」。
3. `.GetPinnableReference()`（ref readonly 方法）→ **BC30657**「method has unsupported return type or parameter types」。
4. 纯 `ref` 成员（C# `ref int`，无 modreq）→ 读值、`= 42` 赋值写回、作 `ByRef` 实参全部正确运行。

**根因（源码核实）**：`ref readonly T` 返回在元数据签名是 `CMOD_REQD([In]) BYREF T`（modreq 在 BYREF 之前，`MetadataDecoder.cs:1193-1198` 解到 `RefCustomModifiers`）。VB 导入层对任何 required modreq 判「不支持」（`Symbol.vb:1106-1107` `ERR_UnsupportedType1`，仅 `IsExternalInit` 豁免；`PEPropertySymbol.vb:124` `AnyRequired()` → `ERR_UnsupportedProperty1`；级联 `Symbol.vb:1008-1019`）。**「用到它的时候自动 dereference」的技术早已存在**（`EmitExpression.vb:1179-1183`，VB15 ByRef 返回机制），只是被导入层的 modreq 拒绝挡住。

**现状后果**：`ReadOnlySpan` 对 VB 是「半个类型」——构造、`Length`、`Slice` 全通，唯独索引器（核心用法）不可用。

**状态**：proposal 与 LDM 会议均为 **Active**；会议 RESOLUTION 采纳核心机制并判 **D4 → P1**。本任务是设计拆分，实现阶段待设计定稿后按 `design-detailed.md` 拆解。

## 2. RESOLUTION 九条吸收映射表

| 决议 | 内容 | 落在本设计 |
|------|------|-----------|
| R1 | 豁免 required `modreq(In)` 导入 + `ReturnsByRefReadOnly` 只读追踪 + 按接收方分类写入语义，不引入声明语法；D4 → P1 | 第 3、4、5 节 |
| R2 | modreq 豁免范围：**统一豁免 required In modreq（返回 + 参数双路径）**，落点 `Symbol.vb:1106-1108` 按 `InAttribute` 身份；顺带解锁虚方法/委托 `in` 参数；`Out` 不豁免 | 第 3.1 节 |
| R3 | 直接赋值拒绝：**复用 `ERR_LValueRequired`(30068)**（延续 30098→30068 既有迁移 `BindingErrorTests.vb:2335`；显式否决 30064）；拒绝面 = 所有 store-through-ref 写路径（直接赋值/复合赋值/Mid 统一检查）；与 R5 显示退化耦合同批落地 | 第 3.3 节 |
| R4 | 丢弃写回：接受（VB copy-out 文化），不警告；spec 硬性文档义务；可选诊断位 default 关闭留实现期评估 | 第 3.3、5 节 |
| R5 | 只读标志暴露到符号层 + 语义模型（承重墙，显示分层不削弱）；SymbolDisplay 合成 `ByRef ReadOnly` 仅限纯 debug 格式（词序/可达路径进 SymbolDisplayTests），IDE 不带（tooltip 退化形态）；**前提「IDE 格式不含 IncludeRef」= 实现期第一验证项，退化不许比纯 ref 更少**；不引入 VB 源语法；仿 C# 加一致性校验 | 第 3.2、3.6 节 |
| R6 | For Each 解锁（实现期验证）；更新 S24 + spec | 第 3.5 节 |
| R7 | §4a readonly 参数识别 v1 不做（copy-out 保底正确） | 第 4、5 节 |
| R8 | `With` 块写穿（复会 2026-09-01 修正）：readonly-lvalue 接收器成员写 `.X = 5` 与链式一致判 **BC30068 拒绝**；值捕获仅服务读 | 第 3.4 节 |
| R9 | 范围修正：`ref readonly` 返回是 C# 7.2（非 12）；路径笔误 `Binder\` → `Binding\` | 第 1、6 节 |

## 3. 总体架构（七步落点）

**核心观察**：缺口极窄——只有一个 `modreq([In])` 挡住整个导入面。修复分两半：**（a）打开导入口子**（豁免 In modreq，读取路径零改动，auto-deref 自动生效）；**（b）锁死写入安全底线**（只读追踪 + 按接收方分类，唯一高危面是漏接线 = 静默写坏只读内存）。七步落点如下。

### 3.1 导入豁免（`VB\Symbols\`，改动点 1）

- `Symbol.vb:1106-1108` `DeriveUseSiteInfoFromCustomModifiers`：把「任何 required modreq 判不支持」收窄为「除 `IsExternalInit`（既有）与 **`InAttribute`**（本次）外的 required modreq 判不支持」——按 **attribute 身份**（`System.Runtime.InteropServices.InAttribute`）豁免，不做「任何 required modreq 都豁免」。模板：C# `Symbol.cs:1293-1354` 的 `AllowedRequiredModifierType` 白名单结构、`PEPropertySymbol.cs:363-367` 的 `IsWellKnownTypeInAttribute()`。
- **双路径**：返回路径 `MethodSymbol.vb:701` 与参数路径 `Symbol.vb:1058` **共用同一函数**——严格「只返回侧」反而要加路径参数更多代码，故统一豁免 In。顺带解锁 C# 虚/抽象方法（`SourceOrdinaryMethodSymbol.cs:131`）与委托（`SourceDelegateMethodSymbol.cs:277`）的 `in` 参数调用。
- `PEPropertySymbol.vb:124`：属性签名 `AnyRequired()` 排除 In modreq。
- `Out` 不豁免（C# 仅函数指针参数允许 Out，VB 不消费函数指针）。

### 3.2 只读标志（`VB\Symbols\`，改动点 2）

- 符号层新增 `ReturnsByRefReadOnly`（`MethodSymbol`/`PropertySymbol` 基类默认 `False`；PE 派生类型从返回 `RefCustomModifiers` 含 `modreq(In)` 判定——数据源 `PEMethodSymbol.vb:1031`、`PEPropertySymbol.vb:136-139`）。
- 语义模型填既有占位：`MethodSymbol.vb:1090-1094`（`IMethodSymbol_ReturnsByReadonlyRef` 硬编码 `False`）、`PropertySymbol.vb:614-618`（`IPropertySymbol_ByRefReturnIsReadonly` 硬编码 `False`）；`RefKind` 映射 `:1096-1100`/`:620-624` 从恒 `Ref` 改为 readonly 时 `RefKind.RefReadOnly`。
- 仿 C# `PEParameterSymbol.cs:415-421` 一致性校验：返回带 `modreq(In)` 但语义不一致（如非 ByRef 返回却带 In modreq）→ 判 unsupported，防第三方乱写元数据。
- **只读标志驱动写入分类**（第 3.3/3.4 节）：VB 侧签名不带 byref（§2 剥离），「对只读返回的写入」不在类型系统天然可见，靠此标志分类。

### 3.3 写入端语义（`VB\Binding\`，改动点 3/4）

按接收方分类，**全程不拒绝合法读取**：

| 接收方 | 处理 | 机制（源码锚点） |
|---|---|---|
| 直接赋值 `s(0) = 5` / 复合赋值 `s(0) += 5` / `Mid(s(0),…) = …` | **编译期拒绝**（复用 `ERR_LValueRequired` 30068） | `AdjustAssignmentTarget`（`Binder_Statements.vb:1936-1940`）对 readonly-lvalue 报 30068；三写路径均经此检查点（直接赋值 `BindAssignment`、复合 `:2073`、Mid `:2271`） |
| `ByRef` 实参（可变接收方） | **copy-out 传副本，写回丢弃** | `Binder_Invocation.vb:2881-2892` 对 readonly-lvalue 跳过直传（`:2887`）改走 copy-out；`LocalRewriter_Call.vb:249-383` 对只读来源省略写回 |
| `ByRef` 实参（`in`/ref readonly 接收方，VB 映射为 `Ref`） | copy-out 传副本（多一次拷贝，零正确性风险；§4a v1 不做零拷贝） | 同上（VB 不区分 readonly 参数，`ParameterSymbol.vb:308`） |
| 非 ByRef（by-value）参数 | 求值传值（auto-deref） | `PassArgumentByVal` + auto-deref `EmitExpression.vb:1179` |
| 类型推断 `Dim x = s(0)` | 褪 ByRef → 值副本 | bound type = 元素类型（§2）+ auto-deref |
| `With` 语句 `With s(0)` | 褪 ByRef → receiver 值捕获（专服务读）；`.Member` 写与链式一致判 **BC30068 拒绝**（复会 R8） | `Binder_WithBlock.vb:238-242` 占位符选择（RValue placeholder）+ `WithExpressionRewriter.vb:337-359` 值捕获读 |

### 3.4 With 块 readonly-lvalue 成员写拒绝（`VB\Binding\Binder_WithBlock.vb`，改动点 5）

`With s(0)` 的 receiver 若是只读 ref 返回，`CaptureInAByRefTemp`（`:324-326`）会把 ref 存进 temp，`.Item(0) = 5` 穿透写坏只读内存。**复会 R8（2026-09-01）**：With 块内对 readonly-lvalue 接收器的成员写 `.X = 5` 与链式 `o.S(0).X = 5` 是同一成员写，判 **BC30068 拒绝**（与既有 RValue 接收器判例一致），不再操作副本——静默改副本是用户不可发现的陷阱。落点：`Binder_WithBlock.vb:238-242` 占位符选择加 `Not boundExpression.IsReadOnlyLValueOrMemberOfReadOnlyLValue()`（成员链版 helper，覆盖 `With o.S(0).Inner`），readonly-lvalue 改走 `BoundWithRValueExpressionPlaceholder`，`.X` 落非 lvalue 复用 `BindAssignmentTarget`/`ReportAssignmentToRValue` 的 30068；`AdjustAssignmentTarget` 不改。**值捕获保留，专服务 `.Member` 读取**（`With o.S(0) : Dim y = .X` 合法）。

### 3.5 For Each 解锁（实现期验证，改动点 6）

`Binder_Statements.vb:4291-4299` 的 `Current` 谓词只查 `IsReadable`，无「不得 ByRef 返回」排除；`LocalRewriter_ForEach.vb:307-320` 把 `Current` 以 RValue 替换进 `controlVariable = Current`，值读走 auto-deref。故 modreq 豁免后 For Each over `ReadOnlySpan` 理论上即通，实现期验证后更新 `ByRefLikeTests.vb:538`（S24）与 `spec-byref-like-safety.md:228-230`。

### 3.6 显示（`VB\SymbolDisplay\`，改动点 7）

`SymbolDisplayVisitor.Members.vb:81-85` 只在 `IncludeRef` 时显示 `ByRef`、无 readonly 显示机制。本提案：`ByRef ReadOnly` 合成**仅限纯 debug / 内部诊断格式**，IDE 显示格式（tooltip）保持退化形态（元素类型，无 byref 无 readonly）——与「VB 侧签名不带 byref」一致，是**应当接受的现状而非缺口**。对照 ref-like **类型**显示（`SymbolDisplayVisitor.Types.vb:445-447`「ByRef Like Structure」，byref-like 任务已落地）：类型栈上唯一的用法独特，用户须一眼识别；而 ref readonly **返回**的用法大多与不带 byref 无异（自动解引用按值用），标了反而噪音，故用户面不标、只留 debug。

## 4. 判定原则

- **不引入声明语法**：VB 不声明 `ref readonly` 返回（无 overrides/implements 需求，D1「只消费不声明」），不加 `scoped`/`UnscopedRef`（M3「VB 不参与 ref-safe-context」）。
- **readonly 判定 = 返回 `RefCustomModifiers` 含 required `modreq(In)`**（attribute 身份），不做「任何 required modreq 都豁免」。
- **写入分类靠只读标志驱动**：直接赋值拒（复用 `ERR_LValueRequired` 30068）、ByRef 实参 copy-out 传副本且写回丢弃、With 块内 readonly-lvalue 成员写拒（复会 R8，与链式一致，值捕获仅服务读）、推断褪 ByRef 操作副本。
- **丢弃写回与字面量传 ByRef 一致**（VB copy-out 文化：写回只在接收方能收写时执行，`vblang\spec\overload-resolution.md:520-522`）；`ref readonly` 返回是只读 lvalue，语义等价于常量位置。
- **修错不算回归（D4）**：从「BC30643/BC30657 拒绝」变为「可读 + 按接收方分类」，纯能力新增；老项目若依赖（不存在的）写穿行为，是修错非回归。

## 5. 代价与边界

- **唯一高危面**：只读追踪漏接线 = 静默写坏只读内存。**P1 地位以「写路径全覆盖拒绝/传副本」为硬验收条件**——测试矩阵必须含直接赋值、复合赋值、Mid、ByRef 实参（可变/readonly 接收方）、With 块、成员链、For Each、类型推断、表达式 lambda/draft rewrite。
- **错误面**：BC30643/BC30657（整个成员不可用）→「可读 + 按接收方分类（直接赋值拒绝 / ByRef 实参 copy-out / With 块成员写拒绝（复会 R8）/ 推断褪 ByRef 值副本）」。纯能力新增，无既有合法代码被改义。
- **丢弃写回的文档义务**：spec 显式声明「写回被丢弃，等价于传常量 ByRef」为硬性文档义务；「callee 实际写副本但写回被丢弃」的可选诊断位（default 关闭）留实现期评估，不阻塞本期。
- **§4a 不做**：readonly 参数识别（`[IsReadOnly]`→`RefKind.In`）v1 不做——copy-out 对只读接收方多一次拷贝但零正确性风险；等性能证据或 `M(s(0))` 成热路径再议。
- **范围修正**：`ref readonly` **返回**是 C# 7.2（`readonly-ref.md`），非 C# 12；C# 12 新增的是 `ref readonly` **参数**。
- **`.vbx` 与 Regular 同一编译器**：规则语义一致，诊断不区分模式；`.vbx` 脚本对现代 .NET 高性能 API 真正可用。
- **行为变化面**：无新语法、无新关键字、不加 `scoped`/`UnscopedRef`；**复用既有 `ERR_LValueRequired`(30068)（零新码、零 xlf 同步）**，与显示退化耦合、同批落地。
