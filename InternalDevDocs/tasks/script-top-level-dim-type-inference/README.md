# 任务：脚本顶层 `Dim` 沿用 `Option Infer` 的推断类型（script-top-level-dim-type-inference）

> 状态：**已收口（F01–F05 全做完，待作者提交）**——甲方案落地 `SourceMemberFieldSymbol.vb` 的 `ComputeType`+`TryComputeScriptFieldType`（`IsScriptClass` 门控）；`ScriptTopLevelDimInferenceTests` 7 格；七门全绿（Semantic 5862→**5869/5765/104**）、L2 **768/0**、档 2 跨提交 `Dim x = 1`→`x.Length` 转编译期 `BC30456`；回收旧"Object"桩；账本 `..\..\upstream-merge.md` §2.25(g)、`spec` 中英、issue 32/21 状态由 main 翻转。问题单：`..\..\issues\issue-script-top-level-field-type-inference.md`（判据/F01 读数 §八、F02 落点 §九）。
> 来历：原问题单 21 挂在"要不要造新能力"上等作者拍板；作者 裁定立项，并给了 C# 实跑读数（`var b = 1;` 之后 `b = "abc";` → `CS0029`）。

## 一、一句话说清要达到的效果

在脚本（`.vbx` 文件或 REPL）顶层写 `Dim x = 表达式` 而不写 `As` 时，`x` 的静态类型应当**与把同样的语句放进普通方法体里的局部变量所得到的类型一致**（受 `Option Infer` 控制），而不是今天这样的 `Object`；并且这个类型**跨提交保持**（在 `#0` 声明、在 `#1` 使用仍知道是 `Integer`）。

## 二、为什么不直接照抄 C#

C# 的 `var b = 1; b = "abc";` 是编译错误（`CS0029`）。VB 在 `Option Strict Off`（默认）下允许 `Integer ← String` 的隐式窄化转换：普通方法体里的同形状**能编译通过、运行时才失败**。若在脚本方言里改成"必报错"，就等于让同一个源文件在脚本里和普通代码里遵守两套赋值规则——那属于「与非脚本 VB 过度分叉」（`..\..\decisions.md` **D7** 分叉纪律），必须避免。
⇒ 所以本任务的判据是**"向 VB 自己的局部变量看齐"**，C# 只作为"字段可以带推断类型、不必退化成 `object`"的方向性证据。

## 三、片段

| 片段 | 做什么 | 验收 |
|---|---|---|
| **F01 真值先行（禁止改码）** | 实测四组读数并记录：① 普通方法体 `Dim b = 1` + `b = "abc"`，`Option Strict Off`；② 同形状 `Option Strict On`；③ 当前码下脚本顶层 `Dim x = 1` 的静态类型（用 `x.GetType()` 或让返回类型参与重载证明是 `Object`）；④ 当前码下"在 `#0` 声明、`#1` 使用"的类型 | 四组读数写进流水账 `tmp\vortex-logs\script-top-level-dim-type-inference\01-truth.md`，其中 ①②即本任务的**目标行为**；③④即要修的现状。取不到 ①② 不许进 F02 |
| F02 实现 | 让顶层 `Dim` 的字段类型取声明处初始化表达式的推断类型。落点候选见问题单 §三-Q2（甲＝`SourceMemberFieldSymbol.vb:186-205` 加脚本类分支；乙＝收集期带类型；丙＝新建 binder 通道，预期先否掉）。**必须带脚本门控条件**（`IsScriptClass`），并写"非脚本面逐字不变"的论证 | 改动只影响脚本类；`Option Infer Off`、显式 `As`、普通类字段三格诊断与产物不变 |
| F03 单元测试 | 正向格：推断出的静态类型（断具体类型/具体值，不是"编译通过"）、跨提交使用不退化、原先抛 `InvalidCastException` 的 LINQ 形状（问题单 21 的原探针）改为断静态类型与结果；反例格：`Option Infer Off` 仍 `Object`、`Option Strict` 两档下赋值不兼容的行为与 F01 的 ①② 逐字一致、普通类字段不变 | 正格断具体值；反例格锁诊断 ID + 位置 |
| F04 回归 | 七门 + L2（**由 main 跑**，子任务不跑全量）+ 重建发布版 `vbi` 跑交互式跨提交形状 | 全绿；基线数字变了就同步 `scripts\verify-vb-compiler-tests.ps1` |
| F05 记账 | `upstream-merge.md` 登记（`SourceMemberFieldSymbol.vb` 是上游同名文件，须写清开关条件与 3-way 注意点）；`spec\spec-scripting-dialect.md` 与 `zh-CN` 补"顶层 `Dim` 的类型推断"一节（含"跟局部变量同规则、不跟 C# 的硬报错走"这句理由）；问题单 32 与 21 状态翻转 | 中英两份同步；状态同步翻转；commit 号不预填 |

## 四、非范围

- 不给普通类/模块的字段加类型推断（VB 语法要求字段写 `As`，本任务不扩语言）。
- 不动 `Option Infer` / `Option Strict` 的默认值与开关语义。
- 不实现 `Const`、`Static` 的推断；不碰 `#Load` 顺序那条线（仍是未登记的候选缺陷）。
- 不改 `SourceMemberFieldSymbol.vb:186-205` 在非脚本容器下的行为。
