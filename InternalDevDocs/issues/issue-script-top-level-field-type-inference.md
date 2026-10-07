# issue 32：脚本顶层 `Dim` 要做类型推断（原问题单 21 的落地版）

- **登记日期**：2026-09-24
- **状态**：**Fixed**（已验证，commit 待作者提交后补）——F01 真值（§八）、F02 落点（§九）→ **甲方案已落地**：`SourceMemberFieldSymbol.vb` 的 `ComputeType`+`TryComputeScriptFieldType`，全程 `IsScriptClass` 门控（普通类字段逐字不变、`Option Infer Off`/显式 `As`/`= Nothing` 保持现状）。F03＝新增 `ScriptTopLevelDimInferenceTests` 7 格（正格断具体静态类型 + 反例锁 + Q1 `BC30512`/`BC30209` 判据）。F04 回归＝七门全绿（Semantic 5862→**5869/5765/104**）、`Scripting\VisualBasicTest` 直跑 **768/0**、档 2 跨提交 `Dim x = 1`→`x.Length` 转编译期 `BC30456`（推断类型跨提交保持）。F05 记账＝`upstream-merge.md` §2.25(g)、`spec`（中英）补"顶层 `Dim` 类型推断"一节、回收 `ScriptModeTopLevelInferenceTests`/`ScriptModeStatementConformanceTests` 里钉"顶层 Dim = Object"的旧桩与问题单 21 的 `InvalidCastException` 形状为正确静态类型正格。
- **来历**：`issue-top-level-field-no-inference.md`（问题单 21）原本按 `decisions.md` D7 的例外 (a) 挂着——"VB 语言层没有字段类型推断，这属要不要造新能力，得作者定"。**作者 裁定：立项，按新 issue 处理** ⇒ 例外 (a) 在这条上不再挡路，剩下的是设计取证与实施。本条把该问题从"挂起"转成"有判据、有验收的工作项"，并收录作者新给的 C# 读数。

## 一、原始症状（问题单 21，已实测，档 2）

脚本顶层写 `Dim x = <表达式>`（不写 `As`）时，**即使 `Option Infer On`，生成的字段类型仍是 `Object`**。后果：后续对 `x` 的成员调用走晚期绑定，形状不匹配时抛 `InvalidCastException`（不是编译错误、也不是"误诊"）。
对照：**普通 VB 的方法体内** `Dim x = <表达式>` 在 `Option Infer On` 下**确实推断类型**（该语言机制存在），差别只在顶层 `Dim` 被提升成字段。

根因位置（读码）：`Compilers\VisualBasic\Portable\Symbols\Source\SourceMemberFieldSymbol.vb:186-205`——类型缺失时的兜底直接给 `Object`，且这段**对容器类型不敏感**（不区分"脚本顶层字段"与"`Class` 里的字段"）。

## 二、作者提供的 C# 读数（档 2：在 `csi` 里跑的）

```
> var b = 1;
> b = "abc";
(1,5): error CS0029: 无法将类型“string”隐式转换为“int”
```

它钉住的不是"要不要推断"，而是**推断结果的性质**：

1. C# 顶层/交互里的 `var b = 1` 推断出 `int`，且该类型**在声明那一刻定死**；
2. 下一条提交里 `b = "abc"` 是**编译错误**（`CS0029`），不是"重新推断成 string"，也不是运行时才炸；
3. 也就是说 C# 的提交字段带的是**推断出来的静态类型**（跨提交时字段类型不塌成 `object`）。这正是 VB 该对齐的形状。

⇒ **D7 里"C# 有、VB 没有 ⇒ 属新语言特性"那句已被推翻**。这里 C# 做的只是"把已有的 `var` 推断结果用在提升后的字段上"，VB 侧对应的现成机制就是 `Option Infer`（已作用于局部变量）。所以本条**不需要发明新语言特性**，需要的是"让顶层 `Dim` 的字段沿用 `Option Infer` 的结果" ⇒ 按 D7 属可移植，例外 (a) 不再适用。

## 三、必须先回答的两个设计问题（不得跳过直接改代码）

### Q1：赋值不兼容时报不报错？——**不许照抄 CS0029 的"必报错"**
VB 与 C# 在这里有一处**语言级差异**：`Option Strict Off`（VB 默认）允许 `Integer ← String` 的**隐式窄化转换**，编译期通过、运行时才失败。所以"推断出 Integer 后 `b = "abc"`"在 VB 里跟**同形状的局部变量**行为一致才是对的，硬报 `CS0029` 式错误等于在脚本方言里偷偷改掉 `Option Strict Off` 的全局语义 ⇒ 属过度分叉（`..\decisions.md` **D7**）。
⇒ **真值先行的第一条**：在**当前码**上实测普通 VB 方法体内的
 `Dim b = 1` + `b = "abc"`，在 `Option Strict Off` 与 `Option Strict On` 下各自的诊断（编译错？哪个码？还是通过并运行期抛？），取到读数后把它作为"顶层 `Dim` 应该对齐的目标"。**这一步没做之前，本条的任何实现都算抢跑。**（档 3 推测：Off 下编译通过并带窄化转换、On 下报 `BC30512` 一类，但**未实测，不得当结论引用**。）

### Q2：字段类型推断发生在哪一层？
候选（按侵入度排序，F01 片段里逐个排除）：
- **甲（最小）**：`SourceMemberFieldSymbol.vb:186-205` 的兜底改成"当容器是脚本类且 `Option Infer` 生效时，用声明处初始化表达式的类型"。风险＝该函数对容器不敏感，改它必须**只对脚本类生效**（`IsScriptClass` 门），否则普通类里"没写 `As` 的字段"（VB 语法上不允许，但错误恢复路径可能撞上）行为会变 ⇒ 非脚本面免疫论证必写。
- **乙**：在收集/合成脚本顶层成员的地方（`SourceMemberContainerTypeSymbol.vb`）带上推断类型，再传给字段符号。
- **丙**：给脚本字段生成 `As` 缺省类型推断 binder 通道（改动最大，先否掉，除非甲乙都证不可行）。
另需确认：**跨提交**读取该字段时类型是否仍是推断出的那个（`Binder_Lookup.vb:858` 沿提交链查找路径拿到的字段符号不能退化成 `Object`）。

## 四、判据（写死这几条，验收逐格对）

1. 顶层 `Dim x = <expr>` 的静态类型 ＝ 同一表达式放进**普通方法体内的局部变量**在同样选项（`Option Infer` / `Option Strict`）下得到的类型。**判据是 VB 自己的局部变量语义**，不是 C# 的 `var` 语义（C# 只在"类型定死、不塌成 object"这点上是方向）。
2. 推断结果**跨提交稳定**：在 `#0` 里 `Dim x = 1`，在 `#1` 里 `x.GetType()` / `x.ToString()` / `x.Length` 的可见性与报错，须与"同类型局部变量"一致；不得仍是 `Object`。
3. 不新增分叉：`Option Infer Off` 时顶层 `Dim x = 1` 仍按现状（`Object`）；`As` 显式写出的字段一字不变；**普通类/模块里的字段不受影响**。
4. 报错与不报错的分界**跟 Q1 的实测读数一致**，不因为"这样更像 C#"而改变。
5. 正例格必须**断具体值/具体静态类型**（例：反射或 `GetType` 结果、或对返回类型做 `Assert.Equal`），不许用"编译通过"或"报了某条诊断"当唯一断言。
6. 回收问题单 21 里的旧形状：那条 `InvalidCastException` 的形状（顶层无 `As` 的 LINQ 查询）修好后必须变成**静态类型正确、能直接成员调用**，并把当时的探针/用例按新判据改成正格，而不是留在临时形状上。

## 五、范围

- **范围内**：顶层 `Dim`（无 `As`）的类型推断 + 跨提交一致性 + Q1 真值 + §四 全部格子。
- **非范围**：不给普通类字段加类型推断（VB 语法要求字段必须写类型，本条不扩语言）；不动 `Option Infer` 的默认值与开关语义；不改 `#Load` 顺序那条线（仍是未登记缺陷，见 `..\..\tmp\HANDOFF.md` §4.6）；不实现 `Const` 推断。

## 六、验证与派工

1. **F01＝真值先行（只读+临时用例，不改产品码）**：Q1 的普通 VB 读数 + 现状顶层 `Dim` 的跨提交读数。没 F01 读数不许进 F02。
2. F02＝实现（按三-Q2 选的落点，带非脚本面免疫论证）。
3. F03＝单元测试（§四 逐格，正向断具体值 + 反例锁：`Option Infer Off`、显式 `As`、普通类字段、非脚本编译）。
4. F04＝回归：七门 + L2 由 main 跑；重建发布版 `vbi` 后跑顶层 `Dim` 的交互式形状（含跨提交）。
5. F05＝记账：问题单 21 状态改成"已由 32 承接"；`upstream-merge.md` 登记（`SourceMemberFieldSymbol.vb` 是上游同名文件）；`spec` 脚本方言文档补"顶层 `Dim` 的类型推断"一节（中英两份）；`decisions.md` D7 例外 (a) 处加一句本条已被作者改判为立项。
6. commit 号一律不预填（作者裁定：main 与子 agent 都不提交、不 `git add`）。

## 七、F01 已经取到的一部分真值（来自仓内既有绿色用例，档 1）

`Compilers\VisualBasicSemanticTest\Semantics\VariableTypeInference.vb:405-444`（`TestOptionInferWithOptionStrict`）已经钉住普通方法体里 `Dim u = 1` 的三档行为：

| 选项组合 | 普通方法体里的 `Dim u = 1` | 证据 |
|---|---|---|
| `Option Infer On` + `Option Strict On` | **合法**，推断为 `Integer`（该用例 `VerifyDiagnostics()` 零诊断） | 同上 `:409-421` |
| `Option Infer Off` + `Option Strict On` | 报 **`BC30209`**（"Option Strict On requires all variable declarations to have an 'As' clause."） | `:423-436` |
| `Option Infer Off` + `Option Strict Off` | 合法、退化成 `Object`（零诊断） | `:438-442` |

⇒ 直接可用的三条约束：① 推断与 `Option Strict On` **不冲突**（脚本侧照此，不要因为 Strict On 就退回 `Object`）；② `Option Infer Off` 时**保持今天的 `Object`**，与本条 §四判据 3 一致；③ `BC30209` 这条报点在脚本顶层"无 `As` 的 `Dim` + Strict On"上现在是什么行为，**仍属 F01 待测**（脚本里顶层 `Dim` 没有 `As` 显然是合法的，说明该报点已被容器判据放过——修推断时别把它带出来）。

**F01 还缺的两组读数**（下一步要跑）：③ 当前码下顶层 `Dim x = 1` 的真实静态类型与"在 `#1` 里读它"的类型；④ 赋值不兼容（`b = "abc"`）在推断已生效的前提下，普通方法体 + `Option Strict Off/On` 各自的诊断（这决定问题单 §三-Q1 的目标行为，也是唯一能定"要不要报错"的证据）。

## 八、F01 剩余读数已取（档 2：用工作树重建的 Debug `vbi` 实跑）

探针与逐字读数存 `tmp\f01-star\`（`mb-off.vbx` / `mb-on.vbx` / `tl-late.vbx`）。

### ④ Q1 目标行为——普通方法体里 `Dim b = 1` 后 `b = "abc"`（顶层 `Dim` 应对齐的真值）
| 选项 | 编译期 | 运行期 | 证据 |
|---|---|---|---|
| `Option Strict Off`（默认） | **通过**（`b` 推断为 `Integer`，隐式窄化 `String→Integer` 放行） | 抛 `System.InvalidCastException: Conversion from string "abc" to type 'Integer'`，栈含 `Conversions.ToInteger` + `Submission#0.Go()` | 已运行 `mb-off.vbx`（exit 2） |
| `Option Strict On` | **报错 `BC30512`**（"Option Strict On 不允许从 'String' 到 'Integer' 的隐式转换"，报在 `b = "abc"` 那行） | —（不产出） | 已运行 `mb-on.vbx`（exit 1） |

⇒ **§三-Q1 定案**：顶层 `Dim x = 1` 修好后，`x = "abc"` 必须与上表**逐字一致**——Strict Off 编译通过、运行期窄化转换报错；Strict On 编译期 `BC30512`。**不得**照 C# 的 `CS0029` 一律硬报错（那会改掉 Strict Off 的全局语义，属过度分叉）。这也把 §七表（推断与 Strict On 不冲突）与"推断生效"接上：推断出 `Integer` 后，Strict On 下正是走 `BC30512` 这条既有报点。

### ③ 当前顶层 `Dim x = 1` 的真实静态类型（修复前）
- 探针 `tl-late.vbx`：`Dim x = 1` + `Console.WriteLine(x.Length)` → **编译通过**、**运行期**抛 `System.MissingMemberException: Public member 'Length' on type 'Integer' not found`（栈顶 `Symbols.Container.GetMembers`，即晚期绑定）。
- ⇒ **实锤：今天顶层 `x` 的静态类型是 `Object`**（若已推断为 `Integer`，`x.Length` 应在编译期报 `BC30456`，而非运行期晚期绑定失败）。这正是 ★ 要改的症状。
- **仍缺的一格（◇ 未单独实测）**：跨提交读取——在 `#0` 里 `Dim x = 1`、`#1` 里 `x.Length` / `x.GetType()` 的静态类型是否与单次一致（预期同一 `SourceMemberFieldSymbol`、同为 `Object`，但需一次 REPL 多提交读数钉死，别当已验证）。

### 结论：F01 已满足进入 F02 的门槛
Q1 目标（对齐 VB 局部语义、非 CS0029）与根因位置（`SourceMemberFieldSymbol.vb:186-205` 对容器不敏感的 `Object` 兜底）均已取证。F02 实施时：让**脚本类**顶层无 `As` 的 `Dim` 字段沿用 `Option Infer` 的结果（甲方案：在 `IsScriptClass` 门内用声明处初始化表达式类型），并保持 `Option Infer Off` / 显式 `As` / 普通类字段三格原样；F03 用例须按本表逐格断具体静态类型（Strict Off 运行期窄化、Strict On `BC30512`、`x.Length` 从晚期绑定转 `BC30456`）。

## 九、F02 落点取证（读码档 3；实施前的机制对齐，未改产品码）

对照 C# 现行实现（同一 fork 树内）钉出甲方案的**最小忠实形状**：

- **C# 在符号层推断脚本字段类型**：`Compilers\CSharp\Portable\Symbols\Source\SourceMemberFieldSymbol.cs:475` `GetFieldType → GetTypeAndRefKind`，脚本分支 `:530 !ContainingType.IsScriptClass` 为假时走 `:548-593`——`binder.BindTypeOrVarKeyword(..., out isVar)`；`isVar` 时：递归守卫 `fieldsBeingBound.ContainsReference(this)` → `ERR_RecursivelyTypedVariable`（`:562-566`）；否则 `new ImplicitlyTypedFieldBinder(binder, fieldsBeingBound)` + `ExecutableCodeBinder(...).BindInferredVariableInitializer(...)`（`:578-583`）绑定初始化器取类型；置 `_lazyFieldTypeInferred = 1`（`:592`）供下游协调"该字段类型是推断来的、初始化器不在别处重算"。
- **VB 缺口（实读确认）**：VB 的 `SourceMemberFieldSymbol.ComputeFieldType`（`SourceMemberFieldSymbol.vb:156-208`）是 `Private Shared`，无 `As` 子句时 `asClauseType = Nothing`，直接 `binder.DecodeModifiedIdentifierType(..., FieldType)`；该解码器对字段**不跑 `Option Infer`**（`Binder_Utils.vb:520-523` 注释逐字："fields do not support Option Infer On / local type inference"），退化成 `Object`。VB 的推断只在**块绑定**期发生（局部变量走 `TryInferVarTypeBlock`，`Binder_Statements.vb:1368-1375` 传 `LocalType` 上下文），而顶层 `Dim` 被提升成字段、其类型在符号计算期就定死了——两条路径不同层，这正是根因。
- **因此甲方案不是"改一行兜底"**，需要在 VB 侧补齐 C# 的三件套：① 脚本类字段符号在类型计算期能**绑定声明处初始化器**取类型（需合适的 binder 通道 + `BindingDiagnosticBag`，避免与后续字段 Value 绑定**重复报诊断/重复求值**）；② **递归守卫**（`Dim x = x` 或相互引用）——VB 现无 `fieldsBeingBound` 线程；③ 诊断/绑定去重协调（对齐 C# 的 `_lazyFieldTypeInferred` 语义）。非脚本面免疫：全程包在 `IsScriptClass` 门内，普通类字段（语法上必须写 `As`）与错误恢复路径不得进入。
- **规模与回归预判**：改后所有脚本类顶层 `Dim`（无 `As`）字段的静态类型从 `Object` 变推断类型 ⇒ 波及 Semantic / Symbol / Emit 三门里**既有断言晚期绑定/Object 形状**的脚本用例（需逐格甄别：哪些是真"应随修复改断静态类型"、哪些是必须保持的原语形状）。⇒ 判 F02 为一个**独立、需完整回归预算**的实施阶段，不与 F01 挤在同一次改动里；先落甲（若 ①②③ 任一证不可行再退乙）。跨提交读数（§八③ ◇ 格）应并入 F03 用例一起钉。
