# Visual Basic Language Design Meeting
August 30, 2026

议题是 `proposal-consume-ref-readonly`——让 VB **消费** C# `ref readonly` 返回。触发点是 ReadOnlySpan 半残：`"abc".AsSpan()(0)`（`ReadOnlySpan(Of T).Item`）报 BC30643、`.GetPinnableReference()` 报 BC30657，而 byref-like 支持（`spec-byref-like-safety.md`）已经让 Span/ReadOnlySpan 的构造、`Length`、`Slice` 全通——索引器是它的核心用法，不消费 = `ReadOnlySpan` 对 VB 仍是半个类型。我们翻源码后发现根因极窄：`ref readonly` 返回在元数据签名带 `modreq([In])`，VB 导入层对任何 required modreq 判「不支持」。「用到它的时候自动解引用」的技术**早就存在**，只是被这一层 modreq 拒绝挡住。这是一次「修一个口子、解锁一整类 API」的会议，但写穿只读内存的安全底线让讨论比表面更深。

## Agenda

* [Proposal: 消费 ref readonly 返回 / Consume Ref Readonly Returns](#proposal-消费-ref-readonly-返回)

## Proposal: 消费 ref readonly 返回

_Related: [`../proposals/proposal-consume-ref-readonly.md`](../proposals/proposal-consume-ref-readonly.md)；基座 `../proposals/proposal-byref-like-safety.md`（D1，VB 只消费不声明）；`../spec/spec-byref-like-safety.md:228-230`（Byref enumerators 限制条目，本提案实现后需修订）；`../decisions.md` D1 / D4 / M3 / M7_

> **来源标注**：本会议引用的编译器源码与规范文本均逐字核对（`文件:行号`）；一处提案路径笔误（`Binder\` → 实为 `Binding\`）与两处提案引用修正（C# 版本归属、CS8331/CS8332）在正文与 RESOLUTION 记录。独立五维评审为不入库工作材料，供主持人/规划内部使用，不混入本官方记录。**复会（2026-09-01）修正 R8**——`With` 块成员写由「值捕获改副本、静默」收紧为「BC30068 拒绝、与链式一致」，正文与 RESOLUTION 相应原地重写；复会由两位独立老登分别复核确认（均强推/高置信），其意见为不入库工作材料，不混入本官方记录。

### 场景与缺口

`ReadOnlySpan(Of T)` 是 .NET 高性能 API 的承重墙，而它的索引器返回 `ref readonly T`——C# 7.2 起的稳定互操作契约（`csharplang\proposals\csharp-7.2\readonly-ref.md`）。提案的现状实证我们复跑过：`.AsSpan().Length` 输出 3（普通属性可消费），`.AsSpan()(0)` 报 **BC30643**「Property … is of an unsupported type」，`.GetPinnableReference()` 报 **BC30657**；而纯 `ref` 成员（无 modreq）读值、`= 42` 写回、作 `ByRef` 实参全部正确运行。缺口是精确的：**不是 ByRef 返回本身被挡，是带 modreq 的 ref readonly 返回被挡**。按 `decisions.md` D4 第二档（C# interop 用例）判 P1 没有悬念，悬念全在「解锁之后写入语义怎么收」。

### 翻源码：根因是一个 modreq

我们把导入链从头摸了一遍，结论收敛得比提案还要干净：

- `ref readonly T` 返回在元数据签名是 `CMOD_REQD([In]) BYREF T`——`MetadataDecoder.cs:1193-1198` 把 BYREF 之前解出的 modifiers 赋给 `info.RefCustomModifiers`（字节 `20 01 1F 55 10 08 08` 复核：`1F`=CMOD_REQD、TypeRef(InAttribute)、`10`=BYREF、`08`=I4——**modreq 确在 BYREF 之前**）。
- 属性路径 `PEPropertySymbol.vb:122-125`：签名任一参数 `RefCustomModifiers.AnyRequired()` → `ERR_UnsupportedProperty1`。
- 方法路径 `MethodSymbol.vb:701` 对 `Me.RefCustomModifiers` 走 `DeriveUseSiteInfoFromCustomModifiers` → `Symbol.vb:1106-1107` 对 required modreq 报 `ERR_UnsupportedType1`（仅 `allowIsExternalInit` 豁免）→ 级联 `Symbol.vb:1008-1019` → `ERR_UnsupportedMethod1`。

我们注意到一个提案没写的先例：**C# 编译器自己的属性导入层 `PEPropertySymbol.cs:363-367` 已经豁免了 InAttribute**——`!m.Modifier.IsWellKnownTypeInAttribute()`。也就是说「modreq In 是良性 required modifier」在 C# 侧是被编进白名单的事实，VB 这次要做的是对齐 C# 既有结构，而不是发明新规则。

提案 §2 的关键主张「读取路径零改动」我们逐锚点复核属实：`BoundPropertyAccess.vb:26` / `BoundCall.vb:26` 用 `isLValue:=…ReturnsByRef` 携带 byref-ness，绑定类型是元素类型（`PEPropertySymbol.vb:138-139` `_propertyType = returnInfo.Type`）；`EmitExpression.vb:1179-1183` `If useKind = UsedAsValue AndAlso method.ReturnsByRef Then EmitLoadIndirect`——**「用到它的时候自动 dereference」早在 VB15 ByRef 返回机制里，只是被导入层的 modreq 拒绝挡住**。这修正了 `spec-byref-like-safety.md:228-230` 的表述：不是「VB does not support properties that return by reference」（纯 ref 已可消费），实际是**带 modreq 的 ref readonly** 才被挡。

### 候选方案

**PROPOSAL A — 豁免 modreq + 只读追踪 + 按接收方分类（采用）。** 导入层认识 required `modreq(In)`；符号层新增只读标志；写入按接收方分类：直接赋值编译期拒绝、`ByRef` 实参 copy-out 传副本且写回丢弃、`With` 块成员写与链式成员写编译期拒绝（复会 2026-09-01 收紧，见 R8）、类型推断褪 ByRef 按值读取。**不引入任何声明语法。**

**PROPOSAL B — 把 ref readonly 当纯 ref（可变）消费（否决）。** 只豁免 modreq、不追踪 readonly，`s(0) = 5` 会被允许——对真正 readonly 内存是**静默内存写坏**。运行期数据损坏不可接受，否决理由与提案一致。

**PROPOSAL C — 引入 C# 完整 ref-safety 模型（否决）。** `scoped`/`UnscopedRef`/逃逸分析/ref local。超出 VB「只消费不声明」定位（D1），消费侧只需「可读、不可写」两态，成本与收益不匹配。`decisions.md` M3 早已锚定「VB 不参与 ref-safe-context」。

**PROPOSAL D — 保持现状（否决）。** `ReadOnlySpan` 索引器不可用，半残；与 D1/M7「消费 C# ref struct 生态」目标冲突。

**PROPOSAL E — 顺带处理 `in`/ref readonly 参数（折叠进范围裁决）。** 提案倾向「范围只收敛到返回」，但我们的翻源码推翻了「in 参数永不带 modreq」的前提，见下。

### 权衡：Q&A

- **丢弃写回 vs C# CS8329 报错：这是本会议最大的跨语言分歧点。** C# 对「ref readonly 返回传 ref/out 实参」是编译期错误 CS8329（「Cannot use … as a ref or out value because it is a readonly variable」，`CSharpResources.resx:3032-3033`）——C# 的立场是宁可显式报错也不静默丢写。VB 提案对可变 `ByRef` 接收方 copy-out 传副本、写回静默丢弃。我们翻 `vblang\spec\overload-resolution.md:520-522` 时确认：**VB 的写回只在接收方能收写时执行，字面量/常量/无 Set 属性传 ByRef 写回本就静默丢弃**——`ref readonly` 返回是一种只读 lvalue，语义等价于常量位置，丢写回是既有规则的第 N 个实例，不是新发明。分歧发生在 VB 语法面而非互操作面：不改元数据、不影响 C#↔VB 互调。**我们接受 VB copy-out 文化分叉**，与字面量一致**不警告**；但「静默」比「拒绝」更隐蔽——callee 确实执行了 `x = 99`（改的是副本）。我们把「在 spec 里显式写成『写回被丢弃，等价于传常量 ByRef』」列为硬性文档义务，并给实现期保留一个「callee 实际写了副本且写回被丢弃」的可选诊断位（default 关闭，与字面量行为一致），不阻塞本期。
- **直接赋值拒绝：复用 `ERR_LValueRequired`（30068），不新增 BC 码，拒绝面必须做成「所有 store-through-ref 写路径」。** 我们翻 `BoundAssignmentOperator.vb:57-61` 确认 ByRef-返回属性作赋值左值时 `AccessKind.Get`——store-through-ref。C# 的直接对应错误码是 **CS8331**（`Binder.ValueChecks.cs:1970-1974`，「Cannot assign to … readonly variable」；`ErrorCode.cs:1518`），**不是提案引用的 CS8332**（那是「赋值给 readonly 变量的成员」）。错误码取舍收敛得很干净，且补上了一层比「显示退化」更强的 VB 基因证据：翻 `BindingErrorTests.vb:2335-2338` 时发现 **VB 早已把整族 read-only 赋值目标从 30098 迁移到 30068**——注释 `' change error 30098 to 30068`（WorkItem 538107），其下 `BC30068ERR_LValueRequired_1`（compilation 名仍叫 `"ReadOnlyProperty1"`）期望输出 30068/30064/30074、**没有 30098**；而 `ERR_ReadOnlyProperty1`(30098) 在本编译器内**无任何上报点，是死码**。30068 不是「新造的通用码」，它就是 VB 对「值样但不可赋」的活实践：`ReportAssignmentToRValue`（`Binder_Expressions.vb:1795-1809`）三分支（常量→30074、readonly 变量→30064、其余→30068），ReadOnly 属性赋值、函数结果接收器的字段赋值今天就走 30068——ref readonly 返回只是这整族的最新成员，沿用是延续而非新造。**为何不复用 30064 `ERR_ReadOnlyAssignment`（`'ReadOnly' variable`）**：其语义槽位看似贴 CS8331，但 `s(0)` 是 ref-返回的属性访问，不是 `ExpressionRefersToReadonlyVariable`（`Binder_Expressions.vb:1811-1837`，只认 FieldAccess/Local）能识别的「variable」，硬送进去会报「'ReadOnly' variable」而用户面连 ReadOnly 字样都看不到——拒绝路径必须显式避开 30064 分支。**这条裁决与「显示退化」耦合**：30068 的「is a value」文案在语义模型层对 readonly-lvalue 确不准（lvalue 有 home），初判因此排除它——但显示退化让用户面现实变成「值样用法」，让 30068 从「按语义不符排除」变回正确选项；复盘也承认按「read-only 语义」关键词搜索（30098/30064 带 `ReadOnly` 字样）漏掉了不带该字样的 30068。**耦合须记录在案并同批落地**：若 IDE 快速信息格式日后证明含 `IncludeRef`（用户看得到 byref 上下文），30068 的「is a value」会突兀——此时一行切 30064（`Binder_Expressions.vb:1805` 的 `err = ERRID...`），实现期先把 30064 备选测试写好。**新增发现（提案未列）**：复合赋值 `s(0) += 5` 与 `Mid(s(0), …) = …` 也是 store-through-ref 写路径——拒绝面若只做「直接赋值」会漏网写坏只读内存。测试矩阵必须覆盖三者。
- **`in` 参数 modreq 边缘：csc 发射矩阵推翻了「本次只豁免返回」的默认。** 提案 Unresolved #4 的实证基础是「系统 csc 的 in 参数无签名 modreq」，`InAttributeModifierTests.cs:220-221` 被引为证据——但翻源码发现 220-221 是**手写 IL 输入**（验证 csc 消费 modreq(In)），不是发射证据。csc 发射 modreq(In) 的真实条件是虚/抽象成员（`SourceOrdinaryMethodSymbol.cs:131`、`SourcePropertySymbol.cs:662`）与**委托恒发**（`SourceDelegateMethodSymbol.cs:277`、lambda 随委托）。即「in 参数无 modreq」只对非虚成员成立，**虚方法/委托的 in 参数确实带 modreq(In)**，VB 现在连这些成员都调不了。而豁免落点 `Symbol.vb:1106-1108` 是返回路径（`MethodSymbol.vb:701`）与参数路径（`Symbol.vb:1058`）**共用**的——严格「只返回侧」反而要加路径白名单参数（`allowIsExternalInit` 先例），更多代码。C# 侧 `Symbol.cs:1261-1267` 允许参数 RefCustomModifiers 带 In。**裁决：统一豁免 required In modreq（返回 + 参数双路径）**，顺带解锁虚方法/委托 in 参数调用；`Out` 不豁免（C# 只对函数指针参数允许 Out，VB 不消费函数指针）。安全性：readonly 来源传 `in` 参数仍走 copy-out（VB 映射 in 参数为 `RefKind.Ref`，`ParameterSymbol.vb:308`），无写穿；「in 参数在语义模型显示为可写 `Ref`」是展示失准而非内存危害，正是 §4a 后续修正的目标。
- **只读标志暴露：成本比提案估计的还低，显示面按格式分层，分层是「消费不声明」定位的必然。** 我们翻 `MethodSymbol.vb:1090-1098` 时发现公共 API `IMethodSymbol_ReturnsByRefReadonly` **已经存在且硬编码 `False`**，`IMethodSymbol_RefKind` 硬映射 `Ref`——「暴露 readonly」不是新造 API，是把既有占位填成真值。C# 侧 `PEParameterSymbol.cs:415-421` 有返回侧一致性契约（「RefReadOnly return should always have this modreq, and vice versa」，不一致判 bad）。**裁决：暴露到符号层 + 语义模型**（`RefKind.RefReadOnly` + `ReturnsByRefReadonly=True`）——这是让分析器/工具读到真实语义、以及一致性校验的基础，显示分层不动这个承重墙。**SymbolDisplay 合成 `ByRef ReadOnly` 只进纯 debug / 内部诊断显示格式，实际给 IDE 的显示格式不带**——tooltip 显示退化形态（元素类型，无 byref 无 readonly，`SymbolDisplayVisitor.Members.vb:81-85` 只在 `IncludeRef` 时才显示 `ByRef`、且无 readonly 显示机制），与「VB 侧签名不带 byref」（提案 §2）一致。我们把退化的道理讲成一条原则：VB 对「能表达或必须约束」的方面显示修饰，对「既不能表达（VB 无 `ByRef ReadOnly` 声明语法）又不必约束（返回值无栈上唯一性）」的通道选择退化——类型层的 `"ByRef Like Structure"`（`SymbolDisplayVisitor.Types.vb:445-447`，`IsRefLikeType` 且 `IncludeTypeKeyword` 时）标注的是一种**类型**，其字段/数组/装箱全禁、用户必须一眼识别；而 ref readonly **返回**的用法大多数时候与不带 byref 无异（自动解引用按值用，`readonly-ref.md:392` 明言值复制是主用法），标了反而是噪音。**两个前提必须在实现期坐实**：其一，tooltip「现状即值样」在本仓库不可直接核实（IDE 快速信息格式定义在 workspace/LSP 层，`Workspaces` 无 `.vb`，只知显示机制层由 `IncludeRef` 门控）——列为实现期第一验证项；其二，纯 `ref` 返回在 `IncludeRef` 下**已经**显示 `ByRef`（`SymbolDisplayTests.vb:5404-5407` 期望 `ReadOnly ByRef P As Integer`），退化显示**不许让更受限的 ref readonly 看起来比可变 ref 更普通**——若 IDE 含 `IncludeRef`，ref readonly 至少应与纯 ref 对等显示。spec 须把「用户面值样」写成意图，防 IDE 日后按 C# 习惯补 `ref readonly` 标记；工具一律读语义模型真值，不从显示字符串推断。合成 `ByRef ReadOnly` 的词序（防与属性描述符 `ReadOnly` 叠成 `ReadOnly ByRef ReadOnly`）与可到达路径（防死代码）须进 SymbolDisplayTests 锁定。并仿 C# 加一致性校验：返回带 modreq(In) 但语义不一致 → 判 unsupported，防第三方乱写元数据。
- **For Each 解锁：无基因障碍、无实现障碍。** 我们翻 `Binder_Statements.vb:4291-4299` 确认 For Each pattern 对 `Current` 的匹配谓词**只查 `IsReadable`、无「不得 ByRef 返回」排除**；降级端 `LocalRewriter_ForEach.vb:307-320` 把 Current 以 RValue 替换进 `controlVar = Current`，值读走 auto-deref 零改动。C# 侧实证 `foreach` over `ReadOnlySpan<T>` 全绿（`CodeGenReadOnlySpanConstructionTest.cs:1643-1651`）。提案引用的 `ByRefLikeTests.vb:538`（S24）已核实：现期望 `ERR_UnsupportedProperty1` + 参数 `System.ReadOnlySpan(Of T).Enumerator.Current`，注释明言根因即 BC30643——本提案落地后该测试从「期望报错」改为「期望通过」，需更新测试与 `spec-byref-like-safety.md:228-230` 的 Byref enumerators 条目。
- **`With` 块内的写穿风险：值捕获保留服务读，成员写与链式一致拒绝（复会 2026-09-01 修正）。** 我们翻 `WithExpressionRewriter.vb:324-359` 确认：`CaptureInAByRefTemp`（`:324-326`）会把 ByRef-返回 receiver 存进 ref temp，`.Item(0) = 5` 会穿透写坏只读内存；而 `:337-359` **已有** `CaptureInATemp` 值捕获路径（readonly 来源走，`:340` `IsReadOnlyLValueOrMemberOfReadOnlyLValue` 分支）。**本轮复会收紧写面**：readonly 接收器在 With 块内的成员写 `.X = 5` 与链式 `o.S(0).X = 5` 是**同一成员写**（spec 11.6 的占位符替换语义，`Binder_Expressions.vb:2637-2641`），应与链式一致报 **BC30068 拒绝**，而不是操作副本——VB 既有 `With (x).F.F.F : .F = ""`（RValue 接收器）早已按 BC30068 拒绝（`WithBlockErrorTests.vb:749-757`），readonly-lvalue 在「能否写穿」上与 RValue 不可区分，静默改死副本是用户不可发现的陷阱。实现落点：`Binder_WithBlock.vb:238-242` 占位符选择加 `Not boundExpression.IsReadOnlyLValueOrMemberOfReadOnlyLValue()`，readonly-lvalue 接收器改走 `BoundWithRValueExpressionPlaceholder`，`.X` 落非 lvalue 自动复用 `BindAssignmentTarget`/`ReportAssignmentToRValue` 的 30068 机制（判定**必须用成员链版 helper**，覆盖 `With o.S(0).Inner`）。值捕获路径保留，专为 `.Member` 读取服务（`With o.S(0) : Dim y = .X` 合法）。测试必须覆盖 With 块内 `.Item` 读写、成员链、ByRef 传递、嵌套写。
- **§4a readonly 参数识别：v1 不做。** copy-out 对所有 ByRef 接收方保底，readonly 接收方多一次拷贝但零正确性风险——「不惧怕拷贝」是 VB 传参文化（属性传 ByRef 本来就拷贝）。做则需要 crack `[IsReadOnly]`/`[RequiresLocation]`/`[Out]` 元数据（C# 参照 `PEParameterSymbol.cs:287-307`，实际映射是 `[IsReadOnly]→In`、`[RequiresLocation]→RefReadOnlyParameter`、`[Out]→Out`，提案「[In]→RefReadOnly」转述不精确），并把 readonly 参数并入不可写追踪——收益是零拷贝、成本是追踪面扩大。**等性能证据或 `M(s(0))`（M 取 `in`）成为热路径再议。**
- **modreq 白名单收窄：按 attribute 身份，不按「任何 required modreq」。** 豁免应锚定 `System.Runtime.InteropServices.InAttribute`（本次）/`OutAttribute`（未来函数指针场景），其余 required modreq 仍判不支持——不破坏「任何 required modreq 判不支持」的既有纪律，只开 ref readonly 这一个口子（VB 老登建议，C# `Symbol.cs:1293-1353` 的 `AllowedRequiredModifierType` 白名单结构为模板）。
- **版本归属修正：** 提案 Summary 写「C# 12 `ref readonly` 返回」误标——`ref readonly` **返回**是 C# 7.2（`readonly-ref.md`），C# 12 新增的是 `ref readonly` **参数**（`ref-readonly-parameters.md`）。返回的元数据编码自 C# 7.2 起稳定，不影响技术正确性，但引用收敛到 7.2。

### RESOLUTION:

1. **采纳核心机制**：豁免 required `modreq(In)` 导入、`ReturnsByRefReadOnly` 只读追踪、按接收方分类的写入语义（直接赋值拒 / ByRef 实参 copy-out 传副本 / With 块成员写与链式一致拒 / 推断褪 ByRef 按值读），**不引入任何声明语法**。D4 → **P1**。
2. **modreq 豁免范围**：**统一豁免 required In modreq（返回 + 参数双路径）**——落点 `Symbol.vb:1106-1108` 按 attribute 身份（`InAttribute`）加入良性白名单；顺带解锁虚方法/委托的 `in` 参数调用（csc 发射实证：`SourceOrdinaryMethodSymbol.cs:131`、`SourceDelegateMethodSymbol.cs:277`）。`Out` 本次不豁免（C# 仅函数指针参数允许 Out）。Unresolved #4 闭合为「本次一并豁免 In」。
3. **直接赋值拒绝**：**复用 `ERR_LValueRequired`（30068）**（「Expression is a value and therefore cannot be the target of an assignment.」），**不新增 BC 码**——与「显示退化」（tooltip 不显示 byref/readonly）自洽、零新码（无上游撞号、无 xlf 同步）；这是**延续 VB 对整族 read-only 赋值目标的既有迁移**（`BindingErrorTests.vb:2335` 注释 `' change error 30098 to 30068`、WorkItem 538107；30098 已退役为死码，`ReportAssignmentToRValue` `Binder_Expressions.vb:1795-1809` 的活实践即 30068）；**明确不复用** `ERR_UnsupportedProperty1/Method1`（元数据拒绝层级）、`ERR_ReadOnlyProperty1`（`'ReadOnly' property` 误导且已死码）、`ERR_ReadOnlyAssignment` 30064（`'ReadOnly' variable` 对 `s(0)` 这类 ref-返回属性访问措辞失准，`ExpressionRefersToReadonlyVariable` 只识别 FieldAccess/Local）。**30068 与 R5 显示退化耦合，须同批落地**：若 IDE 格式含 `IncludeRef`（前提证伪），一行切换 30064（`Binder_Expressions.vb:1805`）。**拒绝面 = 所有 store-through-ref 写路径**：直接赋值 `s(0) = 5`、复合赋值 `s(0) += 5`、`Mid` 赋值——三者统一检查（BoundAssignmentOperator LHS 为 readonly-lvalue 即拒，且不送进 30064 variable 分支）；**With 块内 readonly 接收器成员写（`.X = 5`）经 RValue 占位符路径一并拒（同一 30068，复会 2026-09-01，见 R8）**。测试矩阵必须覆盖。
4. **丢弃写回**：接受（VB copy-out 文化，与字面量传 ByRef 一致），**不警告**；spec 显式声明「写回被丢弃，等价于传常量 ByRef」为硬性文档义务；「callee 实际写副本但写回被丢弃」的可选诊断位（default 关闭）留实现期评估，不阻塞。
5. **只读标志暴露**：暴露 `ReturnsByRefReadonly`/`RefKind.RefReadOnly` 到符号层 + 语义模型（填既有 `IMethodSymbol_ReturnsByRefReadonly`，`MethodSymbol.vb:1090-1094`），供分析器/工具读取真实语义 + 一致性校验——这是承重墙，显示分层不削弱它；**SymbolDisplay 合成 `ByRef ReadOnly` 仅限纯 debug / 内部诊断格式**（词序与可到达路径须进 SymbolDisplayTests 锁定，防与属性描述符 `ReadOnly` 叠字），实际给 IDE 的显示格式不带——tooltip 保持退化形态（元素类型，无 byref 无 readonly，与提案 §2「VB 侧签名不带 byref」一致；前提「IDE 格式不含 `IncludeRef`」为实现期第一验证项，且退化不许比纯 ref 更少——`SymbolDisplayTests.vb:5404-5407` 实证纯 ref 在 `IncludeRef` 下显示 `ReadOnly ByRef P As Integer`），不引入 VB 源语法。仿 C# `PEParameterSymbol.cs:415-421` 加一致性校验（返回带 modreq(In) 但语义不一致 → unsupported）。
6. **For Each 解锁**：通过（实现期验证）。落地后更新 `ByRefLikeTests.vb:538`（S24）从期望报错改为期望通过，并修订 `spec-byref-like-safety.md:228-230`。
7. **§4a readonly 参数识别**：v1 不做（copy-out 保底正确，零拷贝是优化非正确性），列为后续优化。
8. **`With` 块写穿（复会 2026-09-01 修正）**：**readonly 接收器的成员写 `.X = 5` 与链式 `o.S(0).X = 5` 是同一成员写，判 BC30068 拒绝**，不再操作副本——与 S15b 链式、与既有 `With (x).F.F.F : .F = ""` RValue 接收器判例（`WithBlockErrorTests.vb:749-757`）三面一致；「静默改死副本」是不可发现陷阱，废弃。**实现**：`Binder_WithBlock.vb:238-242` 占位符选择加 `Not boundExpression.IsReadOnlyLValueOrMemberOfReadOnlyLValue()`（判定用成员链版 helper，覆盖 `With o.S(0).Inner` 形态），readonly-lvalue 接收器改走 `BoundWithRValueExpressionPlaceholder`，`.X` 落非 lvalue 自动复用 `BindAssignmentTarget`/`ReportAssignmentToRValue` 的 30068 机制，`AdjustAssignmentTarget` 无需改动。**值捕获保留，专服务 `.Member` 读取**（`With o.S(0) : Dim y = .X` 合法，S12/S13d/S13f）。**测试**：S13b/S13c/S13e 从「无诊断 + 副本」翻转为恰一 30068（@ `.X`）；S12/S13d/S13f 读取保持无诊断；补嵌套写与 With 内成员链写。
9. **范围修正**：提案「C# 12 ref readonly 返回」改为 C# 7.2 `readonly-ref.md`；`Binder\` 路径笔误修正为 `Binding\`。

### Implication:

- **能力面**：`ReadOnlySpan(Of T)` 从半残到完整消费——索引器读取、`GetPinnableReference`、For Each 枚举、作 `in` 实参（copy-out 保底）。`.vbx` 脚本对现代 .NET 高性能 API 真正可用。
- **错误面**：BC30643/BC30657（整个成员不可用）→「可读 + 按接收方分类」。纯能力新增，**无既有合法代码被改义**；老项目若依赖（不存在的）写穿行为，是修错非回归（D4 修错原则）。
- **风险面**：只读追踪漏接线 = 静默写坏只读内存，是唯一高危面。**P1 地位以「写路径全覆盖拒绝/传副本」为硬验收条件**——测试矩阵必须含直接赋值、复合赋值、Mid、ByRef 实参（可变/readonly 接收方）、With 块成员写（拒绝，R8）与读取、成员链、For Each、类型推断、表达式 lambda/draft rewrite。
- **文档面**：`spec-byref-like-safety.md:228-230` 的 Byref enumerators 条目与 S24 测试注释（「a general VB limitation」）的表述都不精确，须随实现修订。

### OPEN QUESTIONS / TODO / Follow-up

- **错误码定稿（已闭合）**：复用 `ERR_LValueRequired`（30068），不新增 BC 码（见 RESOLUTION R3；理由：与显示退化自洽 + 延续 30098→30068 既有迁移（`BindingErrorTests.vb:2335`）+ 零撞号 + 零 xlf 同步）。**30064 已显式裁决**：`ERR_ReadOnlyAssignment`（`'ReadOnly' variable`）语义槽位贴 CS8331，但 `s(0)` 不是 `ExpressionRefersToReadonlyVariable` 能识别的 variable，措辞失准，不采用；实现期保留一行切换（`Binder_Expressions.vb:1805`）出口，若显示退化前提证伪或 30068 在语义模型层造成误判即切 30064。
- **IDE 显示退化前提验证**：实现期第一项确认 IDE 快速信息格式是否含 `IncludeRef`（定义在 workspace/LSP 层，本仓库不可直接核实）；若含，ref readonly 至少与纯 ref 对等显示，且 30068 切 30064（见 R3/R5 耦合）。
- **可选诊断位**：「callee 写副本但写回被丢弃」的警告是否落地，实现期评估，default 关闭。
- **For Each 展开验证**：实现期确认 VB For Each 展开对 ByRef-返回属性无额外可写性检查（当前 `Binder_Statements.vb:4291-4299` 未见排除，C# 侧实证成立）。
- **§4a 后续**：`in` 参数映射为 `RefKind.In`（需 crack `[IsReadOnly]` 元数据）以零拷贝直传，独立立项，等性能证据。
- **`GetPinnableReference` 与 C# 15 unsafe-evolution**：C# 侧调用面可能在 requires-unsafe 讨论中，不影响 VB 消费已编译 readonly 返回元数据（M8 观察项）。

### 状态

- **LDM 状态**：**Active**。
- **三态判定：Active**——全部源码声称逐条复核属实（14 个 VB 侧锚点 + 5 个 C# 参照锚点无一虚报，仅一处路径笔误、两处引用修正）；根因收敛到一个 modreq，修复面最小；与 C# 编译器既有结构（`PEPropertySymbol.cs:363-367` 白名单 InAttribute、`Symbol.cs:1261-1267` 参数允许 In、`PEParameterSymbol.cs:415-421` 一致性契约）完全同构，属「对齐 C# 而非发明新机制」；VB 基因贴合度高（2016 LDM ByRef Returns「只消费不声明」的直接续篇，copy-out 丢写回是既有规则实例，直接赋值拒绝是 VB 对只读目标的一贯编译期拒绝，30068 复用更有 `BindingErrorTests.vb:2335` 的 30098→30068 迁移先例支撑）；对 M1–M8 零新摩擦、命中 D4 P1。两处实质扩展（统一豁免 In 参数、复合赋值/Mid 纳入拒绝面）、一处范围修正（C# 7.2）、三处用户面呈现 delta（显示退化、`ByRef ReadOnly` 仅 debug、30068 复用）与一处复会修正（2026-09-01 R8：With 块成员写由「值捕获改副本」收紧为「BC30068 拒绝」）已并入 RESOLUTION；「IDE 格式不含 IncludeRef」为显示退化的外部前提，列为实现期第一验证项。进入实现规划。
