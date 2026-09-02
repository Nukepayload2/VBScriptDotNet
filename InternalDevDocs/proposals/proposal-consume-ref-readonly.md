# 消费 ref readonly 返回 / Consume Ref Readonly Returns

* [x] Proposed
* [ ] Prototype: [Not Started](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [Not Started](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本提案让 VB **消费** C# `ref readonly` 返回（C# 12）：调用返回 `ref readonly T` 的成员、读取其值时自动解引用——例如 `ReadOnlySpan(Of T)` 的默认属性（索引器 `s(0)`）、`GetPinnableReference()`、现代 .NET 高性能 API 的只读 byref 返回。**不引入任何声明语法**：VB 不声明 `ref readonly` 返回（无 overrides / implements 需求），只消费；写入边界按接收方分类处理，保证不写穿只读内存——**直接赋值 `s(0) = 5` 编译期拒绝**，**作 `ByRef` 实参按接收方 copy-out 传副本**（readonly 接收方原样直传、可变 `ByRef` 接收方传副本且写回丢弃、`With`/类型推断褪去 ByRef 操作副本），全程不拒绝合法读取。设计约束：**VB 侧看到的签名不带 byref 修饰**（绑定类型 = 元素类型，byref 经 `isLValue`/`ReturnsByRef` 携带），且 **VB 对无 home 的表达式始终遵从 copy-out 策略**——这两点是本提案语义正确性的核心边界。

现状（实证）：VB 对返回 `ref readonly` 的成员整体拒绝——属性/索引器报 **BC30643**（`ERR_UnsupportedProperty1`），方法报 **BC30657**（`ERR_UnsupportedMethod1`），根因是元数据里 `ref readonly` 返回带 required custom modifier `modreq([In])`，VB 元数据导入对**任何 required modreq** 判「不支持」（仅 `IsExternalInit` 豁免）。纯 `ref` 返回（无 modreq）已可完整消费（VB15 ByRef 返回机制，auto-deref/copy-out/store-through 全通）。

## Motivation
[motivation]: #motivation

- **生态断点（C# interop，D4 → P1）**：C# 12 `ref readonly` 返回遍布现代 .NET 高性能 API。`ReadOnlySpan(Of T).Item` 返回 `ref readonly T`、`ReadOnlySpan.GetPinnableReference()` 返回 `ref readonly T`。VBScript.NET 已通过 `spec-byref-like-safety.md` 让 `Span`/`ReadOnlySpan` 在 VB 侧基本可用（构造、`Length`、`Slice` 等全通），**唯独 ref readonly 返回的成员被挡**——ReadOnlySpan 的索引器是它的核心用法，不消费 = `ReadOnlySpan` 半残。按 `decisions.md` D4（C# interop 用例 → P1）判入。
- **现状断点（实证，`tmp` 用预构建 vbi + 自建 C# 测试库验证）**：
  - `"abc".AsSpan().Length` → 正常输出 `3`（普通属性可消费）。
  - `"abc".AsSpan()(0)`（`ReadOnlySpan(Of T).Item`，ref readonly 索引器）→ **BC30643**「Property 'System.ReadOnlySpan(Of T).Item(index As Integer)' is of an unsupported type」。
  - `.GetPinnableReference()`（ref readonly 方法）→ **BC30657**「method has unsupported return type or parameter types」。
  - 纯 `ref` 成员（C# `ref int` 方法/索引器，无 modreq）→ 读值、`= 42` 赋值写回、作 `ByRef` 实参、`Dim x = GetRef()` 自动解引用，全部正确运行。
- **根因（源码核实）**：`ref readonly T` 返回在元数据签名是 `CMOD_REQD([In]) BYREF T`（**modreq 在 BYREF 之前**，字节实证 `20 01 1F 55 10 08 08`），解码后进 `RefCustomModifiers`（`Compilers\Core\Portable\MetadataReader\MetadataDecoder.cs:1187-1202`）。VB 对任何 required modreq 报「不支持」，两条路径：
  - 属性路径：`Compilers\VisualBasic\Portable\Symbols\Metadata\PE\PEPropertySymbol.vb:122-125` — 属性签名任一参数 `RefCustomModifiers.AnyRequired()` 为真 → `ERR_UnsupportedProperty1`。
  - 方法路径：`Compilers\VisualBasic\Portable\Symbols\MethodSymbol.vb:700-711` 检查返回 `RefCustomModifiers` → `Compilers\VisualBasic\Portable\Symbols\Symbol.vb:1097-1109` 对 required modreq 报 `ERR_UnsupportedType1`（仅 `IsExternalInit` 豁免）→ 级联 `Symbol.vb:1008-1019` → `ERR_UnsupportedMethod1`。
  - 均为**上游 Roslyn VB 行为**，fork 未改动（与上游基准 `0e401fcf` diff 实证：相关文件仅改 byref-like obsolete 参数 / `IsExtensionMember` / SAIM，无 ref readonly 相关）。
- **`in` 参数不在本提案范围（实证）**：C# `in int x` 参数在系统 csc 的签名里**无 modreq**（字节 `10 08` = `BYREF I4`），靠 ParamDefinition 行的 `[In]` 属性区分，VB 已能调用（`WithIn(1)` 实证无错误）。范围只收敛到**返回**；其它 csc 变体若在 `in` 参数签名叫带 `modreq In`，会撞同一条 modreq 检查，作为边缘情况列入 Unresolved。

## Detailed design
[design]: #detailed-design

### 1. 元数据导入：豁免 `[In]`/`[Out]` required modreq（消费入口）

`ref readonly` 返回被拒的**唯一根因**是 `modreq([In])` 被当作 unsupported required custom modifier。最小修复是让导入层认识它（仿既有 `IsExternalInit` 豁免模式）：

- `Symbol.vb:1106-1108` `DeriveUseSiteInfoFromCustomModifiers`：`If Not modifier.IsOptional AndAlso ...` 处追加「modreq 是 `System.Runtime.InteropServices.InAttribute` / `OutAttribute` 时豁免」（类比现有 `IsExternalInit` 分支）。落点即 `allowIsExternalInit` 所在行，扩展为「已知良性 modreq 白名单」。
- `PEPropertySymbol.vb:124` `propertyParams.Any(Function(p) p.RefCustomModifiers.AnyRequired() OrElse p.CustomModifiers.AnyRequired())`：排除 In/Out modreq，其余 required modreq 仍判不支持。
- 解码保持不变：`ref readonly T` 返回 → `ReturnsByRef = True`（`PEMethodSymbol.vb:1011-1013`、`PEPropertySymbol.vb:138-139`），元素类型不带 byref。

修复后 `s(0)`、`GetPinnableReference()` 即可绑定，进入既有 ByRef 返回消费面。

### 2. 签名剥离：VB 侧绑定类型不带 byref（既有机制，无需新造）

VB 对 ByRef 返回的绑定表达，**类型就是元素类型**（byref 剥离），byref-ness 经 `isLValue` 携带：

- `Compilers\VisualBasic\Portable\BoundTree\BoundPropertyAccess.vb:26`：`isLValue:=propertySymbol.ReturnsByRef`。
- `Compilers\VisualBasic\Portable\BoundTree\BoundCall.vb:26`：`isLValue:=method.ReturnsByRef`。
- `PEPropertySymbol.vb:138-139`：`_returnsByRef = returnInfo.IsByRef`、`_propertyType = returnInfo.Type`（元素类型）。

因此 `s(0)` 绑定后类型为 `Integer`、`IsLValue=True`。**读取路径零改动**：`CodeGen\EmitExpression.vb:1179-1183`——`useKind = UsedAsValue AndAlso method.ReturnsByRef` → `EmitLoadIndirect`（自动解引用）。这是本提案的核心收益：**「用到它的时候自动 dereference」技术上已经存在，只是被导入层的 modreq 拒绝挡住**。

```vbnet
Imports System

Sub Main()
    Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10, 20, 30})
    Console.WriteLine(s.Length)    ' 已可用：输出 3
    Console.WriteLine(s(0))        ' 修复后：输出 10（ByRef 返回自动解引用，零改动）
    ' s(0) = 42                    ' 修复后：编译错误（写穿只读返回被拒，见 §4）
End Sub
```

### 3. 只读追踪：`ReturnsByRefReadOnly`（防写穿，安全底线）

若只豁免 modreq、仍按 `RefKind.Ref`（可变）处理，VB 会允许写穿只读返回——对真正 readonly 内存是**静默内存写坏**。必须区分 readonly：

- 符号层新增只读标志（建议 `MethodSymbol`/`PropertySymbol` 的 `ReturnsByRefReadOnly`，或沿用 `RefKind` 扩展到 `RefKind.RefReadOnly`）。导入时由「返回 `RefCustomModifiers` 含 `modreq([In])`」判定（C# 参照：`Compilers\CSharp\Portable\Symbols\Source\CustomModifierUtils.cs:158-161` `HasInAttributeModifier` + `PEParameterSymbol.cs:415-426` 一致性校验）。
- **VB 侧签名不带 byref**（§2）意味着对只读返回的「写入」不在类型系统里天然可见——写入语义由 §4 按接收方分类：直接赋值拒绝、ByRef 实参 copy-out 传副本、With/推断褪 ByRef 操作副本。只读标志驱动这些分类（§4 表逐行接线）。

### 4. 写入端语义：copy-out 传副本，不拒绝（用户定案约束的落点）

VB 对无 home 表达式**始终遵从 copy-out 策略**（用户定案），且**字面量/常量传 `ByRef` 时写回本就静默丢弃**（实证：`P(5)`、`P(7 Const)` 合法、callee 的 `x = 99` 不生效；可写属性 `P(o.Prop)` copy-out 且写回生效 `prop after=99`）。`ref readonly` 返回是一种**只读 lvalue**——读取合法（自动解引用），唯一边界是「不能把它的地址交给会写的接收方」。按接收方分类处理，**全程不拒绝**：

| 接收方 | 处理 | 机制（源码锚点） |
|---|---|---|
| `in`/ref readonly 参数 | **原样传递不 copy**（零拷贝直传 ref；识别 readonly 参数见 §4a） | binder 直传路径 `Binder_Invocation.vb:2887-2890`（lvalue+identity） |
| 其它 `ByRef`（可变）参数 | **copy-out 传副本，写回丢弃**（`temp = s(0)` 解引用取值 → 传 temp → **不写回** s(0)；callee 只改自己的副本） | copy-out 路径 `Binder_Invocation.vb:2892-2942` + `LocalRewriter_Call.vb:249-314`；对只读来源省略写回 |
| 非 ByRef（by-value）参数 | copy-out（自动解引用按值传） | `PassArgumentByVal` + auto-deref `EmitExpression.vb:1179` |
| 类型推断 `Dim x = s(0)` | 褪 ByRef → 值副本 | bound type = 元素类型（§2）+ auto-deref |
| `With` 语句 `With s(0)` | 褪 ByRef → receiver 捕获为**值副本**，`.Member` 操作副本 | `WithExpressionRewriter.vb:324-359`（ByRef-返回 receiver 改走值捕获，见下） |

**直接赋值 `s(0) = 5` 仍拒绝**：这不是传参/With 的副本场景，而是把值**直接写穿**只读 ref（store-through-ref，`BoundAssignmentOperator.vb:59-61`——ReturnsByRef 属性 `AccessKind.Get`，Get 返回 ref 后直接存）。只读来源没有副本可言，必须编译期拒绝（C# 同：CS8332 系）。拒绝时诊断：VB 需要对应错误码/文案，是否复用既有 `ERR_*` 还是新增 BC 码，见 Unresolved。

关键实现点：

- **readonly 来源 → ByRef 实参 = copy-out 且省略写回**。binder 目前对 lvalue+identity 实参**直传**（`Binder_Invocation.vb:2887-2890`），对 readonly 来源必须**降级**为 copy-out 且不生成写回——`RewriteByRefArgumentWithCopyBack`（`LocalRewriter_Call.vb:249-314`）写回那步对只读来源省略。这与字面量传 ByRef 的既有行为完全一致（写回本就丢弃），callee 写的是自己的副本，只读内存永不被写。
- **`With` / `.` 成员访问**（用户特别提示）：`With s(0)` 的 receiver 若是只读 ref 返回，`WithExpressionRewriter.vb:324-325` 的 `CaptureInAByRefTemp` 会把 ref 存进 temp，`.Item(0) = 5` 会穿透写坏只读内存——必须对只读 receiver **改走值捕获**（参考 `:337-359` draft rewrite 已有的 `CaptureInATemp` 值捕获路径），`.Member` 一律操作副本。`obj.Prop.Item(0)` 这类成员链同理。**测试必须覆盖 With 块内 `.Item` 的读写、成员链、ByRef 传递。**

#### 4a. 识别 readonly 参数（可选优化：readonly 接收方零拷贝）

VB 目前把 `in`/ref readonly 参数都映射为 `RefKind.Ref`（`Compilers\VisualBasic\Portable\Symbols\ParameterSymbol.vb:308` `If(Me.IsByRef, RefKind.Ref, RefKind.None)`，忽略 In/Out 区分）。要实现矩阵第一行「readonly 接收方原样传递不 copy」，需读 C# 参数元数据的 `[In]`/`[IsReadOnly]` 属性 → `RefKind.In`/`RefReadOnly`（C# 参照 `Compilers\CSharp\Portable\Symbols\Metadata\PE\PEParameterSymbol.cs:287-307`）。**不做此优化也安全**：所有 `ByRef` 接收方一律对只读来源 copy-out，readonly 接收方多一次拷贝但无正确性风险；做此优化则 `M(s(0))`（`M(in int)`）零拷贝。

### 5. 附带的解锁（待实现期验证）

`ReadOnlySpan(Of T).Enumerator.Current` 是 ref readonly 属性。S24 测试（`ByRefLikeTests.vb:538`）显示 For Each 因 Current 触发 BC30643 被挡；本提案让 Current 变为**可读**的只读属性后，For Each 的 pattern 展开（`GetEnumerator`/`MoveNext`/`Current` 读值）理论上即通。这是附带的正面收益，实现期验证后回填（见 Unresolved）。

### 6. 与既有决策 / 任务的关系

- **D1 / M7**（`decisions.md`）：本提案是「消费 C# ref struct 生态」的补完——byref-like 支持让 `ReadOnlySpan` 基本可用，本提案补上最后一块（ref readonly 返回）。规则与 `spec-byref-like-safety.md` 的「VB 只消费不声明」定位一致：不引入声明语法，不加 `scoped`/`UnscopedRef`。
- **D4**：C# interop 用例 → **P1**。
- **与 byref-like-safety 限制的修订**：`spec-byref-like-safety.md:228-230` 说「VB does not support properties that return by reference」不精确——纯 `ref`（无 modreq）属性/方法已可消费（实证），实际是**带 modreq 的 ref readonly** 才不支持；本提案实现后此限制条目需修订（Specification 阶段）。

## Drawbacks
[drawbacks]: #drawbacks

- **新增只读追踪面**：`ReturnsByRefReadOnly`/`RefKind.RefReadOnly` 需在符号层新增并在赋值/实参两路径接线，比「纯豁免 modreq」多一层改动；漏接线 = 允许写穿只读返回 = 静默内存写坏，属高危面，须测试矩阵覆盖。
- **与 C# ref-safety 的差异**：VB 无 `scoped`/`UnscopedRef`/逃逸分析（D1 定位），本提案只做「消费 + 只读拦截」，不做 C# 的完整 ref 安全模型；`ref readonly` 返回的逃逸语义（如返回的 ref 能否存进 ref local）不在此范围。
- **错误行为变化**：从「BC30643/BC30657 拒绝」变为「可读 + 按接收方分类（直接赋值拒绝 / ByRef 实参 copy-out 传副本 / With、推断褪 ByRef）」，属能力新增，无既有合法代码被改义；若老项目依赖（不存在的）写穿行为，不构成回归（D4 修错原则）。
- **丢弃写回的语义**：readonly 来源传可变 `ByRef` 时 callee 的写被静默丢弃（与字面量传 ByRef 一致，§4）。调用方若以为写会生效会困惑，但这是只读来源的正确语义（callee 本就不该写只读 ref）；与 C# 直接报错不同，VB 走 copy-out 文化——该差异在 meeting 需确认是否接受。

## Alternatives
[alternatives]: #alternatives

- **保持现状（拒绝）**：ReadOnlySpan 索引器不可用，`ReadOnlySpan` 生态价值砍半；与 D1/M7「消费 C# ref struct」目标冲突，与 D4 P1 判入不符。
- **把 ref readonly 当纯 ref（可变）消费**：只豁免 modreq、不追踪 readonly——`s(0) = 5` 会被允许，写穿只读内存，运行期数据损坏，**不采用**。
- **引入 C# 完整 ref-safety 模型**（`scoped`/`UnscopedRef`/逃逸分析/ref local）：超出 VB「只消费不声明」定位（D1），且本提案不需要——消费侧只需「可读、不可写」两态，成本与收益不匹配。
- **顺带处理 `in`/`ref readonly` 参数**：系统 csc 的 `in` 参数无签名 modreq（实证），不构成断点；把参数纳入范围会扩大测试面而无实际收益，**范围只收敛到返回**，参数撞 modreq 的边缘情况在 Unresolved 记录。

## Unresolved questions
[unresolved]: #unresolved-questions

- **直接赋值拒绝的错误码/文案**：写穿 ref readonly 返回（直接赋值 `s(0) = 5`，store-through-ref）用新增 BC 码还是复用既有 `ERR_*`？C# 参照 CS8332「cannot assign to … read-only variable」。倾向新增 BC 码 + 明确文案，待实现期定稿。（ByRef 实参按 §4 copy-out 传副本，不在拒绝面。）
- **只读标志的语义模型暴露**：`ReturnsByRefReadOnly` 是否暴露到 `RefKind.RefReadOnly`（`MethodSymbol.vb:1090-1098` 目前硬映射 `Ref`）？暴露到语义模型/LSP/SymbolDisplay 成本小、对 IDE 悬停正确性有价值；不暴露则 IDE 会把 ref readonly 显示成普通 `ByRef`。倾向暴露。
- **For Each 解锁**：`Enumerator.Current` 变为可读后 For Each over ReadOnlySpan 是否真正通过 pattern 展开（`ByRefLikeTests.vb:538` S24 预期从报错改为通过）？需实现期验证，若 For Each 展开对 ByRef 返回属性有额外检查则单独处理。
- **`in` 参数签名叫带 modreq 的边缘**：其它 C# 编译器/版本若在 `in` 参数签名发 `modreq In`（本 fork 的 csc 测试 `InAttributeModifierTests.cs:220-221` 显示会发），VB 会撞同一条 modreq 检查。是否在本次一并豁免，还是等实证撞到再说？倾向本次只在返回侧豁免、参数侧列入待定。
- **丢弃写回是否警告**：readonly 来源传可变 `ByRef` 时，callee 对副本的写被静默丢弃（与字面量传 ByRef 行为一致，§4）。是否对「callee 实际写了副本、但写回被丢弃」给警告？倾向与字面量一致不警告（保持一致性），但若实现期发现该静默语义有误导风险，可加可选警告，待实现期定稿。

## 相关文档

- 会议：（待 LDM 评估后生成 `../meetings/meeting-consume-ref-readonly.md`）
- 任务：（待 proposal 定稿后拆 `../tasks/consume-ref-readonly/`）
- 决策：`../decisions.md` D1 / D4 / M7
- 参考来源：`../csharplang\proposals\csharp-12.0\ref-readonly-parameters.md`（ref readonly 机制）；`../csharplang\proposals\csharp-7.2\span-safety.md`
- 相关提案：`../proposals/proposal-byref-like-safety.md`（byref-like 支持，本提案的基座）；`../spec/spec-byref-like-safety.md:228-230`（Byref enumerators 限制条目，实现后需修订）
