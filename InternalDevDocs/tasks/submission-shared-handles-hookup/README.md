# 任务：提交类的 `Shared` 事件挂钩要有构造器可注入（submission-shared-handles-hookup）——任务计划

> **状态：待派工**（构建面被 `auto-property-top-level-gate` 占用）。流水账在 `tmp\vortex-logs\submission-shared-handles-hookup\`。缺陷登记：`..\..\issues\issue-submission-shared-constructor-not-run.md`（issue 18，已带「D7 裁定」拆分节）；队列台账 `..\csharp-script-parity-sweep\README.md` **#9**。**子 agent 禁读 `issues\`**。

- **一句话**：本 fork 把 `Handles` 的合法容器扩到了提交类，但共享挂钩的宿主（提交类的 `.cctor`）**在提交类里从不被合成** ⇒ 只有 `Shared Sub … Handles`、没有共享字段初始化器时，挂钩**静默消失**。
- **D7 定位**：C# 无 `Handles`/`WithEvents`/共享事件挂钩概念 ⇒ 三问 ②③ 为「否」，落点选择属**例外 (c)**。作者已授权按 C# 方针自动裁决并不再逐条请示 ⇒ **main 直接选定落点**（下 §一），理由与风险写在案；这条成立**不依赖** C# 判据（fork 自己开了口没接线）。
- **机制（实锤，读码 + blame 归属；日志 `tmp\vortex-logs\csharp-script-parity-sweep\01-dig-sweep-26-18.md`）**
  1. fork `7edb77de9`（2026-09-15）把 `Handles` 合法容器扩到提交类：`Symbols\Source\SourceMemberMethodSymbol.vb:778-780`（逐字注释 "A submission class is a class container and the hookup host … is an instance or shared constructor, which it has as well."），共享挂钩宿主**硬取** `ContainingType.SharedConstructors(0)`（`:795-797`）。
  2. 但 `AddWithEventsHookupConstructorsIfNeeded` 对提交类**整段跳过**：`Symbols\Source\SourceMemberContainerTypeSymbol.vb:2829-2832` 第一行逐字 `If TypeKind = TypeKind.Submission Then 'TODO: anything to do here?`（`git blame` 归上游基线 `e814cb1`）。
  3. 其余造 `.cctor` 的入口都只看"有没有共享初始化器"（`:2747-2752`；`Compilation\MethodCompiler.vb:630` → `:3220-3239`）⇒ 只有共享挂钩时**没有宿主可注入**。注入点本身普通类同形可用：`Analysis\InitializerRewriter.vb:86-134`（按 `MethodKind` 匹配宿主，`:111`）。
  4. VB 自己要求"共享挂钩落在 `.cctor` 时必须按时执行"：`Emit\NamedTypeSymbolAdapter.vb:482-492` 对这种形状**抑制** `beforefieldinit` ⇒ 惰性口径不是本条的免责理由。

## 一、main 裁定的落点（自动裁决，不问）

**采纳：把 `TypeKind.Submission` 纳入 `SourceMemberContainerTypeSymbol.vb:2829-2912` 的挂钩-构造器合成** —— 存在共享 `Handles` 挂钩时 `EnsureCtor(isShared:=True)`，复用 fork `48d8edbff` 已修好的**无参**提交类共享构造器路径（`:2744-2752` 逐字注释 "A shared constructor cannot take the submission array parameter…"），并确认 `NamedTypeSymbolAdapter.vb:482-492` 的 `beforefieldinit` 抑制在提交类上生效 ⇒ 由宿主 `newobj` 提交类实例触发 `.cctor`，与普通类同机制。

**否掉的次选**：把共享挂钩挂进脚本初始化器（`Sub Main` / `<Initialize>`）。理由：`Handles` 是**类型级**语义，脚本初始化器每次 `ContinueWith` 都新建实例 ⇒ 同一事件被**重复挂钩**（处理器多次触发），并把类型级语义降级为实例级。

## 二、范围内

| # | 单元 | 说明 |
|---|---|---|
| F01 | 真值先行 | **第一步必须是实测**：写用例钉死"只有 shared `Handles`、无共享字段初始化器"的提交类到底 ①`SharedConstructors` 为空还是 ②有集合但 `SharedConstructors(0)` 越界（裸索引在没建成时可能是 ICE 而不是静默丢弃——取证**未复现**到该形状，属推测）；同时用 `.vbx` 探针复现 issue 记录的"零诊断、stdout 为空"（REPL 逐条提交会把块切开，取证的 4 条 REPL 探针因此未复现，别再用 REPL 试这个形状） |
| F02 | 实现 | 按 §一 落点接线；改动面预计 `SourceMemberContainerTypeSymbol.vb`（提交类分支）+ 必要时 `SourceMemberMethodSymbol.vb:795-797` 的宿主取值改为"没有就不挂钩并给出普通类会给的那条诊断"（**不得新造诊断**） |
| F03 | 单元测试 | `test-plan.md` §一：投递计数（脚本 / 普通类对照各一，断"恰好一次"）、`RunClassConstructor` 前不投递、共享字段初始化器与共享挂钩共存时两者都生效；**不得**用"顶层读一下那个字段"的写法构造（那是既有实锤指出的真空控制） |
| F04 | #9-A 的文档与对照 | 「`Shared` 字段初始化器惰性 = 与 C# 同形的 CLR 语义」写进 `spec`（由 main 落笔，EN 正本 + `zh-CN` 对等），并补正向对照用例：读 ⇒ 断到值；不读 ⇒ 无副作用 |
| F05 | 回归与账本 | 七门 gate + `Scripting\VisualBasicTest` `-automated` + 重建宿主复跑 `tmp\probes\u6bc\`、`tmp\probes\u10\` 的既有探针；issue 18 按 A/B 两半分别收口（A 记「与 C# 同形，不修」，B 记 Fixed，commit 不得预填）；`upstream-merge.md` 入账（两处上游同名文件） |

### 非范围
- 不改 `beforefieldinit` 的**默认**口径（A 半边已裁：与 C# 同形，保持惰性）。
- 不解决"普通类型方法调用触发 `.cctor`、提交类方法调用不触发"的 CLR 层原因（属 A 的解释线，需要 IL/TypeDef 反汇编另立项，不阻塞 B）。
- 不碰解析层、`Binder_Expressions.vb`、`OverloadResolution.vb`（另三条任务的面）。

## 三、风险与停止上报
1. `.cctor` 体内不得使用 `Me`；`Analysis\InitializerRewriter.vb:118-129` 已按 `addHandlerMethod.IsShared` 分支，但提交类里 `RaiseEvent`/`AddHandler` 的 receiver 走 `BoundMyBaseReference` 的 Dev10 兼容分支需复核 ⇒ 复核不出来就停手上报。
2. 合成次序：`SharedConstructors` 必须在 `GetHandles` 取宿主**之前**建好；若实测发现次序不成立（同一轮 `MembersAndInitializers` 内的先后），停手交 main，不硬调顺序。
3. 构建面独占（BC2012）。
