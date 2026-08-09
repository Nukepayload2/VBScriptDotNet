# C# Language Design — Interop 索引（供 ModVB / VBScript.NET 评估用）

## 头部

- **本文件用途**：为 ModVB 102 个提案的 meeting agent 提供 dotnet/csharplang 官方仓库中 **C# interop 方向**的浓缩背景与文件地图，节省逐个读库的 token。
- **如何使用**：
  1. 先读「一、C# interop 现实方向摘要」快速建立世界观；
  2. 若某主题与手头提案相关，到「二、关键文件索引」用 Windows 反斜杠路径 + Grep 关键词深挖原文；
  3. 手头提案的「VBScript.NET 应对」应参考 **`modvb\decisions.md`（VBScript.NET 设计决策记录）** 的 M1–M8 节；C# 事实仍以本文件为准。
- **来源目录**：`csharplang\`（dotnet/csharplang 官方仓库镜像，main 分支，含 2013–2026 LDM notes、proposals、spec）。Governance 见该目录 `README.md`、`Design-Process.md`、`Language-Version-History.md`。
- **版本对照**（Language-Version-History.md）：C# 7.2=Span/ref-like、7.3=unmanaged 约束、8.0=构造 unmanaged/栈上 stackalloc、9.0=nint/nuint+函数指针+SkipLocalsInit+Source Generators、11.0=ref fields/scoped/UnscopedRef+static abstract+数值 IntPtr、12.0=inline arrays+ref readonly params、13.0=ref struct interfaces+allows ref struct+ref/unsafe in iterators/async+params collections、14.0=first-class Span+extensions、15.0（开发中）=unsafe evolution+unions+closed hierarchies。

---

## 一、C# interop 现实方向摘要（分主题）

### T1 治理与节奏
- 语言由 C# LDT 在 Roslyn 中开发；提案需被 LDT 成员 champion，经 LDM 讨论后进 milestones（Working Set/Backlog/Any Time/Likely Never）。已完成 feature 归档到 `proposals/csharp-X.0/`，最终进 ECMA-334（ECMA 滞后数年）。
- 节奏约每年一个主版本（.NET 同步），低层/interop feature 往往先做编译器原型再要求 runtime/ECMA 配合。
- 含义：C# 是 CLR 新特性与 .NET 生态的**主要推动者**，低层能力常「先语言成型，再 runtime 跟上」。

### T2 低层内存互操作：Span / ref struct / ref fields（核心主线）
- 从 C# 7.2 `Span<T>` 出发，C# 持续往「栈上安全低层类型」投入：ref fields + `scoped` + `[UnscopedRef]`（C# 11，目标之一是用 C# 重写 `Span<T>`、移除 runtime 内部 `ByReference<T>` 特判类型）、`ref readonly` params（C# 12）、`ref struct` interfaces + `allows ref struct` 泛型反约束（C# 13）、first-class Span 类型/隐式 span 转换（C# 14）。
- 方向本质：**在不放弃类型/内存安全的前提下，把「接近指针」的能力做成安全语言特性**，让 .NET 库（BCL）与第三方都能做高性能类型。
- 相关文档见 proposals: csharp-7.2/span-safety, csharp-11.0/low-level-struct-improvements, csharp-13.0/ref-struct-interfaces, csharp-14.0/first-class-span-types, proposals/expand-ref（未来）, proposals/ref-struct-closures（未来）。

### T3 指针 / unsafe / 函数指针 / nint（显式不安全通道）
- `unmanaged` 约束（C# 7.3）+ 构造泛型 unmanaged（C# 8）：让泛型可复用于非托管类型（blittable）。
- `nint`/`nuint`（C# 9，C# 11 数值化）：为指针/句柄提供原生大小整数；动机原文：「The motivation is for interop scenarios and for low-level libraries.」（native-integers.md）。
- 函数指针 `delegate*<...>`（C# 9）：暴露 `ldftn`/`calli` IL，支持 unmanaged 调用约定（Cdecl/Stdcall/Thiscall/Fastcall/自定义 CallConv + SuppressGCTransition），与 `[UnmanagedCallersOnly]` 配合；原文动机：「expose IL opcodes that cannot currently be accessed efficiently… `ldftn` and `calli`」（function-pointers.md）。
- `[SkipLocalsInit]`（C# 9）、stackalloc 扩展（7.3/8）、custom `fixed`/GetPinnableReference（7.3）、module initializers（C# 9）、指针 to managed 类型（C# 11 放宽，有 warning）。
- 方向本质：**为「确实需要裸指针的高性能/互操作代码」提供显式、可控的通道**；这些能力集中在 `unsafe` 上下文内。

### T4 与现有非托管生态 / COM 的互操作
- 仓库内直接讨论 COM 的会议少且浅：LDM-2014-09-03（out/ref 哑元参数在 COM interop 场景）、LDM-2026-04-20（closed hierarchies 与 sealed 类型→接口转换，提及 COM 例外）。
- 现代方向是**用 source generator 替代运行时 marshaling**：P/Invoke 的 `[LibraryImport]`（见 unsafe-evolution 提案引用的 Roslyn 生成器）、COM source generators（不在本库，属 dotnet/runtime 生态）。
- 语言侧主要支持点：函数指针调用约定、`UnmanagedCallersOnly`、`ref struct`/inline array 布局、NoPIA（C# 4 的 embedded interop types，Language-Version-History 仍列为历史 feature）。
- 含义：C# 语言层面对 COM 的**新增投入很少**，重心转向 AOT 友好的 source-gen 互操作。

### T5 AOT / trimming / 反射互操作（生态压力）
- csharplang 仓库本身不主导 NativeAOT（属 dotnet/runtime），但 LDM 多次围绕 AOT/trimming 做语言取舍：
  - LDM-2021-05-12：top-level 程序与 single-file/trimming 设置属于项目级问题。
  - LDM-2023-07-24：interceptors 的动机之一正是「反射型场景难以 AOT 兼容」，ASP.NET 在 .NET 8 把拦截器作为 AOT 场景的依赖；LDM 倾向「把信息放回类型系统」而非类型系统外 hack。
  - LDM-2025-07-30、LDM-2026-02-09 等（closed hierarchies / 类型系统能力）均受「AOT 下少反射、可静态推理」的压力塑造。
- 方向本质：**类型系统承担更多本来靠反射/动态完成的职责**（unions、closed hierarchies、source-gen、interceptors），以服务 NativeAOT 与 trimming。

### T6 Source generators / 元编程
- Source Generators（C# 9）+ Incremental Generators（C# 10）：把「生成代码」从运行时反射移到编译期；interceptors（experimental，见 proposals/csharp-12.0/experimental-attribute + working-groups/interceptors）可在 AOT 下做方法拦截。
- 方向本质：**编译期生成替代运行时动态**，同时给互操作（P/Invoke、COM、序列化）提供「零反射」出口。

### T7 dynamic / 表达式树（晚期绑定）的相对边缘化
- `dynamic`（C# 4）与 Expression trees（C# 3）长期无大演进；C# 14 仅放宽了表达式树里的可选/具名参数。
- first-class-span-types 明确点出表达式树与 Span 的冲突（ref struct 无法被解释器支持）；unsafe-evolution 的 open question 甚至问「dynamic 是否应标 unsafe」（因为反射类 API 在 AOT 下不安全）。
- 方向本质：**动态/晚期绑定不是 C# 的前进方向**，反而被 AOT/trimming 视为负担；C# 靠类型系统与 source-gen 取代之。

### T8 C# 15/16 未来方向（2025–2026 LDM 主线）
- **Unsafe evolution**（proposals/unsafe-evolution.md，C# 15 候选）：把 `unsafe` 从「出现指针」改为「解引用非托管内存」；指针类型/固定缓冲/地址运算将**不再需要 unsafe 上下文**，只有解引用仍需；成员级 `unsafe` 变为 *requires-unsafe*（调用方需 unsafe 上下文），新增 `safe` 关键字与 `unsafe(expr)` 表达式；程序集级 opt-in（MemorySafetyRulesAttribute）。**对 VB 的原文表述**：「We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either.」（unsafe-evolution.md）
- **Unions / closed hierarchies / discriminated unions**（C# 15 主线）：让类型系统表达「一组封闭类型」，服务模式匹配与 AOT。
- **Extensions**（C# 14 已部分落地；C# 15 续）：扩展现有类型，冲击 VB 的 extension 方向。
- 这些方向**大幅改变 C# 与 VB 的「默认安全模型」差异**（对 VBScript.NET 的含义见 `modvb\decisions.md` M8）。

---

## 二、关键文件索引（深挖用）

路径均为 Windows 反斜杠；关键词供 Grep 定位。

### 提案（proposals）— 已完成 feature
| 路径 | 一句话要点 | 关键词 |
|---|---|---|
| `csharplang\proposals\csharp-7.2\span-safety.md` | ref-like 类型仅存栈上的安全规则（ref-safe-context/safe-context）源头 | ref struct, safe-to-escape, ByReference |
| `csharplang\proposals\csharp-7.3\blittable.md` | `unmanaged` 约束：原文「make it easier to author low level interop code」；mod-req 保护 | unmanaged, blittable, constraint |
| `csharplang\proposals\csharp-8.0\constructed-unmanaged.md` | C# 8 把 unmanaged 扩展到构造泛型类型 | unmanaged generic |
| `csharplang\proposals\csharp-9.0\function-pointers.md` | `delegate*`、ldftn/calli、unmanaged 调用约定、UnmanagedCallersOnly | calli, CallConv, SuppressGCTransition |
| `csharplang\proposals\csharp-9.0\native-integers.md` | nint/nuint=IntPtr/UIntPtr 别名；动机为 interop+低层库 | nint, nuint, IntPtr |
| `csharplang\proposals\csharp-9.0\module-initializers.md` | `[ModuleInitializer]` 程序集加载前执行 | ModuleInitializer |
| `csharplang\proposals\csharp-9.0\skip-localsinit.md` | `[SkipLocalsInit]` 省零初始化 | SkipLocalsInit, localsinit |
| `csharplang\proposals\csharp-11.0\low-level-struct-improvements.md` | ref fields + `scoped` + `[UnscopedRef]`；用 C# 重写 Span<T>、移除 ByReference<T>；out 隐式 scoped；RefSafetyRules(11) 模块属性 | ref field, scoped, UnscopedRef, RefSafetyRules |
| `csharplang\proposals\csharp-11.0\numeric-intptr.md` | nint/nuint 数值化（C# 11） | nint, IntPtr operators |
| `csharplang\proposals\csharp-11.0\static-abstracts-in-interfaces.md` | 接口静态抽象成员（泛型 math 基础） | static abstract, IAdditionOperators |
| `csharplang\proposals\csharp-12.0\inline-arrays.md` | `[InlineArray(N)]` 类型安全内联数组，Span 访问 | InlineArray, fixed buffer |
| `csharplang\proposals\csharp-12.0\ref-readonly-parameters.md` | `ref readonly` 参数（声明处）+ 调用点规则表 | ref readonly, in, out |
| `csharplang\proposals\csharp-13.0\ref-struct-interfaces.md` | ref struct 可实现接口；`allows ref struct` 反约束；不可 box、DIM 限制 | ref struct, allows ref struct, DIM |
| `csharplang\proposals\csharp-13.0\ref-unsafe-in-iterators-async.md` | async/iterator 在无 await/yield 段内可用 ref/unsafe；迭代器不再自动 unsafe 上下文 | ref in async, iterator unsafe |
| `csharplang\proposals\csharp-13.0\params-collections.md` | `params ReadOnlySpan<T>` 等集合 | params span |
| `csharplang\proposals\csharp-13.0\overload-resolution-priority.md` | `[OverloadResolutionPriority]` 调 API 重载优先级（span 时代规避歧义） | OverloadResolutionPriority |
| `csharplang\proposals\csharp-14.0\first-class-span-types.md` | 隐式 span 转换（数组/Span/ReadOnlySpan/string 互通）；波及重载解析与表达式树 | implicit span conversion, betterness, expression trees |

### 提案（proposals）— 未来/开发中/候选
| 路径 | 一句话要点 | 关键词 |
|---|---|---|
| `csharplang\proposals\unsafe-evolution.md` | C# 15：重定义 unsafe=解引用非托管内存；成员级 requires-unsafe；`safe` 关键字；unsafe(expr)；**含对 VB 明确表态** | caller-unsafe, requires-unsafe, safe, MemorySafetyRules |
| `csharplang\proposals\expand-ref.md` | 扩展 ref/scoped（ref scoped 参数）→ 更多 ref struct 场景 | ref scoped, lifetime |
| `csharplang\proposals\ref-struct-closures.md` | lambda 转 `allows ref struct` 接口的分配无关闭包（NLinq 方向） | ref struct closure, IFunc |
| `csharplang\proposals\async-method-ref-parameters.md` | async 方法 ref/ref-like 参数（排队中） | ref in async |
| `csharplang\proposals\fieldof.md` | `fieldof` 表达式（字段句柄） | fieldof |
| `csharplang\proposals\unsafe-evolution.md`、`csharplang\proposals\capability-safe.md`、`csharplang\proposals\unsigned-sizeof.md` | unsafe/能力安全/无符号 sizeof 相关 | capability, sizeof |
| `csharplang\proposals\csharp-12.0\experimental-attribute.md` | `[Experimental]`（interceptors 等实验特性依赖） | Experimental, interceptors |

### 会议（meetings）— 按主题
- **函数指针/低层**：`csharplang\meetings\2018\LDM-2018-09-05.md`、`csharplang\meetings\2018\LDM-2018-10-15.md`、`csharplang\meetings\2019\LDM-2019-10-30.md`、`csharplang\meetings\2020\LDM-2020-12-02.md`（关键词：function pointer, UnmanagedCallersOnly, delegate*）。
- **ref fields/span**：`csharplang\meetings\2017\LDM-2017-05-16.md`、`csharplang\meetings\2018\LDM-2018-09-24.md`、`csharplang\meetings\2020\LDM-2020-10-12.md`、`csharplang\meetings\2020\LDM-2020-10-14.md`、`csharplang\meetings\2021\LDM-2021-04-07.md`、`csharplang\meetings\2021\LDM-2021-05-10.md`、`csharplang\meetings\2023\LDM-2023-01-11.md`（关键词：ref field, scoped, ref struct）。
- **unsafe evolution（2025–2026 最重要）**：`csharplang\meetings\2025\LDM-2025-09-17.md`、`LDM-2025-10-29.md`、`LDM-2025-11-05.md`、`LDM-2025-11-12.md`；`csharplang\meetings\2026\LDM-2026-01-21.md`、`LDM-2026-04-01.md`、`LDM-2026-04-06.md`、`LDM-2026-04-13.md`、`LDM-2026-04-22.md`、`LDM-2026-04-29.md`、`LDM-2026-05-13.md`、`LDM-2026-05-27.md`、`LDM-2026-07-22.md`（关键词：unsafe, safe, extern, LibraryImport, requires-unsafe）。
- **unsafe evolution 工作组文档**：`csharplang\meetings\working-groups\unsafe-evolution\unsafe-simple-core-model.md`（最浓缩）、`unsafe-evolution-compatibility-proposal.md`、`incremental-unsafe.md`、`unconditionally-safe-pointers.md`、`unsafe-alternative-syntax.md`。
- **AOT/trimming/interceptors**：`csharplang\meetings\2021\LDM-2021-05-12.md`、`csharplang\meetings\2023\LDM-2023-02-27.md`、`csharplang\meetings\2023\LDM-2023-07-24.md`、`csharplang\meetings\2023\LDM-2023-10-04.md`、`csharplang\meetings\2025\LDM-2025-07-30.md`、`csharplang\meetings\working-groups\interceptors\IC-2023-03-20.md`、`IC-2023-04-04.md`（关键词：AOT, trimming, interceptor, source generator）。
- **source generators**：`csharplang\meetings\2020\LDM-2020-03-30.md`、`LDM-2020-04-01.md`、`LDM-2021-04-07.md`、`csharplang\meetings\2022\LDM-2022-02-09.md`（关键词：source generator, incremental）。
- **COM**：`csharplang\meetings\2014\LDM-2014-09-03.md`、`LDM-2014-10-01.md`、`csharplang\meetings\2020\LDM-2020-12-02.md`、`csharplang\meetings\2026\LDM-2026-04-20.md`（关键词：COM, IDispatch, interop；注意内容浅）。
- **unions/closed hierarchies（C# 15）**：`csharplang\meetings\working-groups\discriminated-unions\*.md`（含 allows.md、union-proposals-overview.md、Runtime Type Unions.md）、`csharplang\meetings\2025\LDM-2025-08-18.md`（C# 15 Kickoff）。
- **extensions（C# 14/15）**：`csharplang\meetings\working-groups\extensions\*.md`、`csharplang\meetings\working-groups\roles\*.md`。

### 规范（spec）— 注意：spec 目录只是链接索引，正文已迁到 dotnet/csharpstandard
- `csharplang\spec\unsafe-code.md`：指针/固定缓冲/栈分配的章节链接（§22 unsafe code）。
- `csharplang\spec\types.md`、`csharplang\spec\structs.md`、`csharplang\spec\conversions.md`、`csharplang\spec\delegates.md`、`csharplang\spec\attributes.md`、`csharplang\spec\interfaces.md`：类型/结构/转换/委托/属性/接口规范入口。

---

## 三、对 VBScript.NET 的含义 → 已移至 `modvb\decisions.md`

> 原「三、对 ModVB / VBScript.NET 的含义（M1–M8）」已按用户指示独立成文，见 **`modvb\decisions.md`**（VBScript.NET 设计决策记录）。该文件同时记录决策修正（ref struct 解法、NativeAOT 桥、postfix-casting 语义）。本索引只保留 C# interop 事实。

---

## 四、引用纪律提示

- 引用 C# 原文必须**逐字准确**并标注来源文件（用上述路径）；无法核实的标注 **Suspect** 或列入 **OPEN QUESTIONS**。
- 本索引中已核实可引用的原文与出处（供 meeting agent 直接使用）：
  - 「The motivation is for interop scenarios and for low-level libraries.」→ `csharplang\proposals\csharp-9.0\native-integers.md`（Summary）。
  - 「…expose IL opcodes that cannot currently be accessed efficiently, or at all, in C# today: `ldftn` and `calli`.」→ `csharplang\proposals\csharp-9.0\function-pointers.md`（Summary）。
  - 「The primary motivation is to make it easier to author low level interop code in C#.」→ `csharplang\proposals\csharp-7.3\blittable.md`（Motivation）。
  - 「We do not need to add support to Visual Basic for *requires-unsafe* members since there are no `unsafe` contexts in VB today and no way to work with pointers there either.」→ `csharplang\proposals\unsafe-evolution.md`（「VB」小节）。
  - 「The main reason for the additional safety rules when dealing with types like `Span<T>` and `ReadOnlySpan<T>` is that such types must be confined to the execution stack.」→ `csharplang\proposals\csharp-7.2\span-safety.md`（Introduction）。
  - 「The inability for `ref struct` to implement interfaces means they cannot participate in fairly fundamental abstraction techniques of .NET.」→ `csharplang\proposals\csharp-13.0\ref-struct-interfaces.md`（Motivation）。
- **OPEN QUESTIONS**（本索引未深挖、需自行核实的点）：COM source generator 的具体语言协作（属 dotnet/runtime，本库无正文）；NativeAOT 与 trimming 的精确语言层约束；interceptors 的最终状态（实验性，可能 pull）。
