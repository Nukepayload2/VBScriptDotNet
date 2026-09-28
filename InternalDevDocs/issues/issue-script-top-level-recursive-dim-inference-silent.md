# issue 33：脚本顶层 `Dim` 的类型推断撞循环时静默退 `Object`，而 C# 侧实跑是报错（CS7019）

- **登记日期**：2026-09-24（main）
- **状态**：**Fixed**（已验证，commit 待作者提交后补）——落点甲落地：`SourceMemberFieldSymbol.vb` 的守卫支先报 `BC30980` 再退 `Object`（净增 9 行）。计划 `..\tasks\script-top-level-recursive-dim-inference\`、流水账 `..\..\tmp\vortex-logs\script-top-level-recursive-dim-inference\`、账本 `..\upstream-merge.md` §2.25(g)（同文件改写而非新增）、规格中英两份已补（`spec\spec-scripting-dialect.md` 顶层 `Dim` 一节）。**档位**：七门（Semantic 5914/5810/104/失败 0）、L2 直跑 769/0、档 2 宿主四格对账＝**主线亲跑**；子 agent 的自 dump 读数凡主线未复跑的一律标 ◇（见 §七 与 `05-implementer-rd-f03.md`）。
- **性质**：**诊断缺失**——一种输入形态被静默降级，不崩、不误诊。按 D7 属"C# 更硬"的一侧。
- **与 issue 32 的关系**：**不是 32 引入的回归**。改前顶层 `Dim` 恒 `Object`，循环形状同样零诊断；32 让非循环形状对齐之后，这个缺口才显形为"该报而未报"。

## 一、复现条件与逐字读数（两侧同树、同形状）

**必须同一棵树内多行**（一个 `.vbx` / 一个 `.csx`）。逐条 REPL 提交各自成树，后一条的名字在前一条里根本不可见，永远形不成循环——拿那种读数下"两侧一致"的结论无效。

### VB 侧（已运行）

宿主 `Interactive\vbi\bin\Debug\net10.0\vbi.exe`，构建时刻 2026-09-24 18:44，晚于被改文件 `Compilers\VisualBasic\Portable\Symbols\Source\SourceMemberFieldSymbol.vb`（15:57）⇒ 该读数带工作树改动。

```vb
Imports System
Dim a = b
Dim b = a
Console.WriteLine("V6 " & a.GetType().Name)
```

编译期**零诊断**；运行期 `System.NullReferenceException`，栈顶 `Submission#0.VB$StateMachine_1_<Initialize>.MoveNext()`，指向探针第 4 行。

自指格 `Dim a = a` 之后 `Console.WriteLine(a.GetType().Name)`：同判（零诊断 + NRE）。

读数判读：若 `a` 真被推断成 `Integer` 这类值类型，`GetType()` 会打出 `Int32` 而不会 NRE；若是错误类型，编译期就会报错。**零诊断 + NRE 这两条合起来定住的是"静默退成 `Object`、初值 `Nothing`"**。

### C# 侧（已运行）

对照器 `csi.exe` 版本 `5.10.0-1.26380.3`——与 `decisions.md` D7「C# 侧证据」那条 CS0027 读数同一版本。

```csharp
var a = b;
var b = a;
```

`(1,5): error CS7019: 无法推理"a"类型，因为其初始值设定项直接或间接地引用定义。`，exit=1。自指 `var a = a;` 落同一条码、同一位置格式。

### 对照组（证明 32 的主体已对齐，不属本条范围）

| 形状 | C# csi | VB |
|---|---|---|
| `a = 5` 之后 `a.Length` | `(2,28) error CS1061："int"未包含"Length"的定义` | `BC30456："Length"不是"Integer"的成员` |
| 非循环前向引用 `a = b` / `b = 5`，打印类型与值 | `Int32 val=0` | `Int32 val=0`（逐字相同） |

⇒ 类型层沿字段链穿透、值层按源码序求值这两点两侧同形；**只有循环那一格分叉**。

探针源码与各自 `.out` 留档在 `tmp\probes-cyc\`（`p1-cycle` / `p6-self` / `p3-infer-check` / `p5-fwd` 与 `c1-cycle` / `c2-fwd` / `c3-infer` / `c4-self`）。

## 二、根因（已检查）

- **VB**：`SourceMemberFieldSymbol.vb` 的重入守卫 `_computingScriptFieldType`（声明 `:24`，读 `:118`，置位/复位 `:142`/`:170`）命中时 `Return Nothing` ⇒ 直接退回旧的 `Object` 兜底，**不报任何东西**。
- **C#**：`SourceMemberFieldSymbol.cs` 的 `fieldsBeingBound.ContainsReference(this)` 分支报 `ERR_RecursivelyTypedVariable`（`Compilers\CSharp\Portable\Errors\ErrorCode.cs:1165` = 7019），随后落 `CreateErrorType("var")`。

## 三、影响面

脚本模式下**无 `As`** 的顶层 `Dim`，其初始化器直接或间接引用自身（含两个顶层字段互指、含自指）。

普通 VB **不受影响，且循环在普通上下文根本形不成**：方法体内 `Dim a = b`（`b` 尚未声明）实测报 **`BC32000`「局部变量"b"在声明之前不能被引用」**，`Option Explicit` 开／关两档同判 ⇒ 与 `Option Explicit` 无关，是作用域报点。本条的分叉点因此正落在"顶层 `Dim` 被提升为脚本类字段、成员查找不看声明顺序"这一脚本特有机制上。

## 四、判据（不随落点变的部分）

1. 只对脚本类生效；普通方法体继续由 `BC32000` 挡在前面，本条不得改变它。
2. 报的是"类型推不出来"，与赋值兼容性无关 ⇒ **不触碰** 32 的硬边界①（不得照抄 `CS0029` 那类硬报错去改 `Option Strict Off` 的全局语义）。
3. 正例格必须断到具体诊断（码 + 位置），不许用"编译失败"当断言。

## 五、这是"今天静默、修复后会报错"的形状

按本清单顶部约定登记在此：落地后新增的是一条**编译期错误**，此前能编译并运行（读到一个 `Nothing`）的脚本形状会开始被拒。须补的回归用例见 §六末与 §七。

## 六、落点（main 已按 D7「(c) 的处置」自动裁＝甲；留痕）

- **选：甲** —— 守卫命中即报既有码 `ERR_CircularInference1`＝**BC30980**，文案 `Type of '{0}' cannot be inferred from an expression containing '{0}'.`（唯一活报点 `Binder_Expressions.vb:3154`，服务于局部推断）。零新码、不动 `VBResources.resx`。
- **否：乙**（新增码或改写文案以覆盖互指）—— 占新 BC3xxxx 需走提案，改文案则动上游同名文件 `VBResources.resx`（按 `upstream-merge.md` 口径要记账并增加合并冲突面）；而 C# 侧对"直接／间接"两种循环**同样只用一个 CS7019** ⇒ 要对齐的是"报不报"的语义强度，不是措辞精度。
- **风险**：甲的成立前提尚未证——字段类型计算期报出的诊断能否到达用户、位置对不对（现状试绑时诊断被丢进 `BindingDiagnosticBag.Discarded`）。BC30980 在 `ErrorFacts.vb:550` 所属清单的性质（是否 BuildOnly／IDE 隐藏）也未查。
- **实施第一步必须钉死的实测项**：见 §七.1，以及下面这条前置门——**F01 用既有报点做旁证**（脚本顶层 `Dim x As NoSuchType` 的 `BC30002` 正是从 `ComputeType` 内报出的）实跑证明"类型计算期能报出去"，判不通即停手，不许临场改用乙或自造丙。
- **明示接受的代价**：互指形状的文案偏松（`a` 的初始化器里并不含 `a`）。记在此处与任务计划 §二，不静默；措辞精化单列为**不动码**的后续项。

D7 三问核对：①VB 有现成表达（BC30980）②判据在本仓树内（`ErrorCode.cs:1165` ＋ `SourceMemberFieldSymbol.cs`）且两侧均已实跑 ③不落在 VB 专有概念上 ⇒ 可移植，自动裁，不回给人。

## 七、实施第一步必须钉死的实测项

1. **先 grep 有没有既有绿色用例把"顶层 `Dim x = x`／互指 ⇒ 零诊断"钉成期望值**（32 之前恒 `Object`，这类钉桩可能存在）。找到就按 D7 的测试回收线一并回收成"被拒 + 消息指向真原因"，**不许**用 revert 修复来翻绿。
2. 反例锁同步补齐：普通方法体 `BC32000` 一格、`Option Explicit` 两档同判一格、非循环前向引用仍 `Int32`/`0` 一格（防"修循环"顺手把前向引用也变严）。
3. `spec\spec-scripting-dialect.md` 的顶层 `Dim` 推断一节要写清循环形状的归属，中英两份同步（先改英文正本再回灌译文，不得反向）。

## 八、收口实测（2026-09-24）

- **实现**：`SourceMemberFieldSymbol.vb:118-129`，守卫支加一行 `diagBag.Add(ERRID.ERR_CircularInference1, Me.Syntax.GetLocation(), Me.Name)` 后再 `Return Nothing`；产品净增删 **9 / 0**（`git diff HEAD --numstat` 亲读）。
- **§七.1 的普查结论**：命中 **0 处**既有钉桩把"顶层自指／互指 ⇒ 零诊断"当期望值（RD-F03 复核前由 RD-F01 普查 187 文件 / 6 条检索式得出）⇒ **无回收义务**。不许动的近邻钉已列在任务计划 §三.4。
- **七门（main 亲跑）**：全部门 失败 0；Semantic **5914 / 5810 / 104 / 0**（本条新增 28 格＝`ScriptTopLevelRecursiveDimInferenceTests`），余六门数字与基线一字未变；基线抬升由 main 在 `scripts\verify-vb-compiler-tests.ps1` 落笔，抬前的实测与预测逐字相符。
- **L2（main 亲跑，直跑 `-automated`）**：**769/0**（总数未变＝本条未在 L2 面新增格）。
- **档 2 宿主（main 亲跑，重建 Debug `vbi`）**：`p1-cycle.vbx(2) : error BC30980: 无法从包含“a”的表达式中推断“a”的类型。`——一条、波浪线落在标识符（`col=5`、`spanLen=1`）、名字**裸渲染**（不是 `Private a As Object`）⇒ §六 约束② 在产品路径上也成立；`p6-self` 同形；哨兵 `p5-fwd` 仍 `type=Int32 val=0`、`p3-infer-check` 仍 `BC30456` ⇒ 没有改严。`exit` 由 0 转 1。
- **明示接受的代价（不变）**：互指形状的文案说"包含 `a` 的表达式"，而 `a` 的初始化器里其实只有 `b`；C# 的措辞明写"直接或间接"。本轮不动 resx（§六 否乙的理由仍成立）。
- **两条继承未复验（◇）**：① "改前 `count=0`"那半边——RD-F03 因不许临时换产品码而没能复跑，本条"改前"依据是 RD-F01 的 `q1d`/`q1e`（main 亲验两文件均 0 字节）＋ RD-F02 的自 dump；② RD-F02 独立探针的**原始条数**带夹具产物（`BC50001` 隐藏未用 `Imports`、`BC42367`），单测夹具不产生 ⇒ 不得照抄进断言（RD-F03 已按自己夹具里的读数重新定桩）。
- **顺带消掉的一处旧瑕**：`TryComputeScriptFieldType(diagBag)` 的 `diagBag` 形参原先全程未用（死参数），本条之后成为报点通道。
