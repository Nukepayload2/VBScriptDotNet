# spec：REPL 表达式开头 ? 可选

**状态**：Active（LDM 会议 2026-08-09 三态判定）；所属里程碑：2.0 beta（`with-modified-vbsyntax` 分支，进行中）。

## 能力规范

- 交互式 REPL 中，裸表达式作为提交，按**绑定结果形态**判定：绑定结果为**值引用**（`BoundKind.Local` / `Field` / `Parameter` / `PropertyAccess` / `LateInvocation`+PropertyGroup）→ 自动求值打印；绑定结果为**方法组/真正调用**（`BoundMethodGroup`→无参调用 / `BoundCall` / `LateMemberAccess`）→ 保持合法语句不打印。显式 `?` 前缀保留，行为兼容不变。
- **变量名求值打印**：`Dim before = Now` 后直接敲 `before`（或其它变量/字段/属性名）即自动打印其值，对齐 C# `csi`；无括号 Sub 调用（`MySub`）仍为合法语句不打印。
- 本身合法的语句（赋值、带副作用调用、无括号 Sub 调用）**不改义、不打印**；仅**提交末尾**的表达式产生结果（多行提交中间行的裸表达式仍报错）。
- 自动打印为**交互式专属**：脚本模式（`.vbx`）不启用；脚本文件末尾表达式不设退出码，与 2.0 已修复的 Function Main 退出码语义（`Return 42` → 42；裸 Return/无 Return → 0）**正交**。

## 结构事实

- **编译器层落点**：解析层把语句首裸表达式解析为表达式语句（区分「裸数值+冒号 = 标签」与「裸表达式 = 表达式语句」）；**裸标识符解析为裸表达式**（`IdentifierName`，不包成 `X()`）。绑定层对提交**末尾**的表达式语句绑定，**按绑定结果形态（`BoundKind`）判定触发**——值引用（`Local` / `Field` / `Parameter` / `PropertyAccess` / `LateInvocation`+PropertyGroup）→ 打印；方法组/真正调用（`MethodGroup`→无参调用重分类 / `Call` / `LateMemberAccess`）→ 保持语句不打印。触发锚定「绑定结果形态」，而非「所有末尾表达式」或「错误码枚举」——Void Sub 调用等合法语句不受影响。
- **宿主层零改动**：`HasSubmissionResult` + `globals.Print` 现有路径自动生效（编译器层产出「无错 + 非 Void 末尾表达式语句」后统一走打印）。
- **`.vbx` 代价（已接受）**：`SourceCodeKind.Script` 同时服务 `.vbx` 与交互提交，`.vbx` 裸表达式从「报错」变「静默 no-op（不设退出码）」，随 C# 对 csx 的先例接受（LDM-2020-04-15）。
- 文件级引用：解析层 `Compilers\VisualBasic\Portable\Parser\`、绑定层 `Compilers\VisualBasic\Portable\Binding\Binder_Statements.vb`、宿主 `Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs`。实现细节（改动函数、行号、改动形状）以 `tasks/optional-question-prefix/design-detailed.md` 为唯一来源，本 spec 不堆行号。

## 规范依据链

proposal（`proposals/proposal-optional-question-prefix.md`，Active）→ LDM 会议（`meetings/meeting-optional-question-prefix.md`，RESOLUTION #1-#5）→ 本 spec。实现细节见 `tasks/optional-question-prefix/design-detailed.md`（F2 详细设计）。

## 测试事实

- 无副作用测试矩阵（REPL / 脚本模式 / Regular 零影响三块）：`tasks/optional-question-prefix/design-detailed.md` §7。
- 测试纪律：单测无副作用（不发起网络、不写文件、不启动进程、不写注册表）；REPL 用例走内存 `CreateRunner(input:=...)` + `TestConsoleIO`。
- 实现期测试项：畸形脚本（`Now +`）错误码偏移（BC30800 类 →「缺失表达式」类 BC3xxxx）需补回归用例。

## 与 modvb 的关系

本 spec 描述 VBScript.NET 产品自身能力，与 modvb 提案库分离；实现细节来源为产品自身设计文档，不依赖 modvb 原文。
