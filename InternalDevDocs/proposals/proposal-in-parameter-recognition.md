# 识别 in 参数并零拷贝直传只读来源 / In Parameter Recognition (R7)

* [x] Proposed
* [ ] Prototype: [Not Started](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [Not Started](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本提案让 VB **识别 C# 的 `in` 参数**（只读引用参数），把只读来源——`ref readonly` 返回，如 `ReadOnlySpan(Of T).Item`（`s(0)`）——传 `in` 参数的路径从 copy-out 传副本改为**零拷贝直传**，对齐 C# 语义。当前 VB 把 `in`/`ref`/`ref readonly` 参数一律映射为 `RefKind.Ref`（`ParameterSymbol.vb:304-310`），只读来源传 `in` 参数走 copy-out（多一次拷贝）；C# 侧同一调用是零拷贝直传（只读引用直接取地址）。本提案是**优化**：copy-out 保底正确，零拷贝不改变语义，只消除多余拷贝。

本提案是 `meeting-consume-ref-readonly.md` RESOLUTION **R7** 的独立立项提案。R7 裁决「v1 不做、copy-out 保底正确、零拷贝是优化非正确性、独立立项，等性能证据」——本提案记录该优化立项所需的设计依据与 C# 证据链。

## Motivation
[motivation]: #motivation

- **C# 语义对齐**：C# 侧 `span[0]`（`ReadOnlySpan<T>.Item`，`ref readonly T`）传 `M(in T)` 是**零拷贝直传**——规范 `readonly-ref.md:322`「ref readonly returns never return via a local copy」、`:247`「in arguments are always passed as direct aliases … Temporary is never used」；编译器 `EmitAddress.cs:177-182` `UseCallResultAsAddress` 对 `RefKind.RefReadOnly` 调用直接作地址。VB 现状多一次拷贝。
- **性能**：`M(s(0))`（`M` 取 `in int`）从 copy-out 变零拷贝；`in` 参数是 C# 高性能 API（`ReadOnlySpan<T>` 生态）的默认互操作契约，`ref-readonly-parameters.md:42` 以 `ReadOnlySpan<T>..ctor(in T value)` 为例。等性能证据（`M(s(0))` 成为热路径）触发立项。
- **语义模型显示修正**：`in` 参数在语义模型从「可写 `Ref`」修正为「只读 `In`」——C# `in` 参数本就只读，变 `In` 是展示修正而非行为破坏（`meeting-consume-ref-readonly.md:48` 判断「展示失准而非内存危害」）。

## Detailed design
[design]: #detailed-design

### C# 侧语义（设计基准，证据链来自 C# 老登聚焦调查）

C# `in` 实参按「是否产生临时拷贝」分三态（规范 `readonly-ref.md` + C# 编译器源码双实锤）：

| 实参类别 | 示例 | C# 行为 | 拷贝 |
|---|---|---|---|
| 可变 l-value | `int x` | 直接取地址 | 零拷贝 |
| 只读 l-value | `span[0]`（ref readonly 返回）、`readonly` 字段、`in` 参数 | 直接取地址（只读） | 零拷贝 |
| RValue | `5`、按值方法返回、`default(T)` | `EmitAddressOfTempClone`：求值→存临时→取临时地址 | 拷贝 |

三态在 C# 发射层 `EmitAddress` 收敛为一句：「有 home（l-value，含只读）→ 直传；无 home（RValue）→ `EmitAddressOfTempClone`」。`AddressKind` 枚举 `Writeable < Constrained < ReadOnly < ReadOnlyStrict`（`CodeGenerator_HasHome.cs:33`）决定。

关键规范文本（逐字）：
- `readonly-ref.md:282-284`：「For all purposes a `ref readonly` member is treated as a `readonly` variable … It is permitted to pass them as `in` arguments, but not as `ref` or `out` arguments.」
- `readonly-ref.md:291`：`Method2(in Method1()); // valid`（正例）。
- `readonly-ref.md:322`：「`ref readonly` returns never return via a local copy … always direct references.」
- `readonly-ref.md:247`：「`in` arguments are always passed as direct aliases when call-site uses `in`. Temporary is never used in such case.」
- `readonly-ref.md:248`：「…When argument is not an LValue, a temporary may be used.」——l-value（含只读）直传是行为，RValue 才许可临时。
- `readonly-ref.md:128`：「`in` arguments guarantee _aliasing_ of the argument variable. The callee always receives a direct reference to the same location as represented by the argument.」
- `readonly-ref.md:122-123`：「it is valid to pass `readonly` fields, `in` parameters or other formally `readonly` variables as `in` arguments.」

C# 编译器实现锚点：
- `EmitAddress.cs:110-119` + `177-182` `UseCallResultAsAddress`：`RefKind.RefReadOnly` 调用结果直接作地址（零拷贝）。
- `EmitAddress.cs:328-336` `EmitAddressOfTempClone`：RValue 拷贝进临时。
- `EmitExpression.cs:724-732`（byref 实参断言不 clone 进临时）+ `738-754`（`In→AddressKind.ReadOnly`）。
- `LocalRewriter_Call.cs:1269-1272`（无修饰符→`In`、显式→`StrictIn`）。
- `Binder_Expressions.cs:3199`：`in` 实参要求 `BindValueKind.ReadonlyRef`（区别于 `ref/out` 的 `RefOrOut`）——只读 l-value 合法作 `in` 实参的绑定依据。
- `PEParameterSymbol.cs:291-306`：元数据映射 `[IsReadOnly]→In`、`[RequiresLocation]→RefReadOnlyParameter`、`[Out]→Out`。

### VB 实现（三段对齐，C# 老登建议）

1. **符号层：crack `[IsReadOnly]` 元数据 → `RefKind.In`。** 镜像 C# `PEParameterSymbol.cs:291-306` 的映射顺序（`[Out]→Out`、`[RequiresLocation]→RefReadOnlyParameter`、`[IsReadOnly]→In`、其余→`Ref`），替换 VB `ParameterSymbol.vb:304-310` 的 `IParameterSymbol_RefKind`（现 `If(Me.IsByRef, RefKind.Ref, RefKind.None)`，忽略 In/Out）。
2. **绑定层：readonly l-value → `in` 参数合法化。** VB 实参绑定处放行「只读 l-value → `In` 参数」为直传（C# 靠 `BindValueKind.ReadonlyRef`，`Binder_Expressions.cs:3199`）。现 VB 一律当 `RefKind.Ref`（可写接收方），只读 l-value 不满足可写 → copy-out。
3. **发射层：`in` 参数 + ByRef-返回 readonly 实参走引用直传。** VB 发射层 auto-deref 只在「used as value 时 EmitLoadIndirect」（`EmitExpression.vb:1179-1183`），作实参时天然是引用直传——会议记录实证「纯 ref 成员作 ByRef 实参正确运行」。识别 `In` 后，只读来源不再被 copy-out 拦截。

**元数据语义正交性**：`[IsReadOnly]` 是 ParamDefinition 行的属性，`modreq(In)` 是签名自定义修饰符——本提案的 `[IsReadOnly]` 识别与父提案 R2 的 `modreq(In)` 豁免**正交**，互不依赖。

### 与父提案的关系

- 父提案 `proposal-consume-ref-readonly.md` 使 `ref readonly` 返回可消费；本提案消除只读来源传 `in` 参数时的多余拷贝。
- 会议 `meeting-consume-ref-readonly.md` R7 已精确转述元数据映射「`[IsReadOnly]→In`、`[RequiresLocation]→RefReadOnlyParameter`、`[Out]→Out`」（提案曾转述「[In]→RefReadOnly」不精确）。

## Drawbacks
[drawbacks]: #drawbacks

- **追踪面扩大**：readonly 参数需并入不可写追踪（父提案 R5 只追踪返回侧，本提案扩展到参数侧）。
- **行为/显示变化**：`in` 参数在语义模型从「可写 `Ref`」变「只读 `In`」；凡是依赖「`in` 参数 = 可写 ByRef 接收方、写回丢弃」的既有 VB 代码会受影响——C# `in` 参数本就只读，属展示修正而非行为破坏。
- **元数据 crack 成本**：需处理 `[IsReadOnly]`/`[RequiresLocation]`/`[Out]` 三属性的区分与边界。
- **收益是性能非正确性**：无性能证据前投入不划算；copy-out 保底正确。

## Alternatives
[alternatives]: #alternatives

- **维持 copy-out（现状，v1 正确）**：所有 ByRef 接收方对只读来源一律 copy-out，readonly 接收方多一次拷贝但零写穿风险。等性能证据或 `M(s(0))`（M 取 `in`）成热路径再议（会议 R7 条件）。
- **全部 ByRef 参数做 RefKind 识别**：一并识别 `Out`/`RefReadOnlyParameter`/`In`——改动面更大；本提案只收敛到 `In`（零拷贝收益最明确）。
- **在导入层只放行、不暴露语义模型**：只做发射优化不改 `RefKind` 显示——语义模型仍报告可写 `Ref`，分析器误读只读性，不采纳（与父提案 R5「只读标志暴露到语义模型」一致）。

## Unresolved questions
[unresolved]: #unresolved-questions

- **性能证据标准**：何时算「`M(s(0))` 成为热路径」？需要什么基准触发立项？
- **`[RequiresLocation]`（`ref readonly` 参数，C# 12）是否一并识别**：C# 12 的 `ref readonly` 参数与 `in` 参数的直传规则一致（`ref-readonly-parameters.md:66`），是否纳入本提案范围？
- **委托/虚方法 `in` 参数的直传**：父提案 R2 顺带解锁虚方法/委托 `in` 参数调用后，其零拷贝直传是否同路径适用？
- **readonly 字段 / `in` 参数作实参的边界**：C# 允许 readonly 字段直传 `in`；VB 侧是否需同样放行（VB 源声明无 readonly 字段 byref 消费场景）？

## 相关文档

- 会议：`../meetings/meeting-consume-ref-readonly.md`（RESOLUTION R7「独立立项，等性能证据」；`:48`/`:52`/`:81`）
- 父提案：`proposal-consume-ref-readonly.md`（消费 ref readonly 返回；§4a 识别 readonly 参数）
- C# 规范：`../csharplang\proposals\csharp-7.2\readonly-ref.md`、`../csharplang\proposals\csharp-12.0\ref-readonly-parameters.md`
- 调查材料（git-ignored）：`../tmp\meetings\consume-ref-readonly\csharp-veteran-in-refreadonly.md`（C# 老登聚焦调查：三态绑定 + 规范/源码证据链）
