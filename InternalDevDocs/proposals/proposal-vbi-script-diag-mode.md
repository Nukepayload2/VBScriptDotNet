# vbi 脚本模式诊断检查模式 / VBI Script Mode Diagnostics-Only Switch (`/diag`)

* [x] Proposed
* [ ] Prototype: [Not Started]
* [x] Implementation: Complete
* [x] Specification: [Complete](../spec/spec-vbi-script-diag-mode.md)

## Summary
[summary]: #summary

vbi 脚本模式新增开关 **`/diag`**:把 `.vbx` 脚本作为脚本 submission **只编译、输出完整编译器诊断(错误 + 全部警告)、不执行、不发射**。语义对标 `tsc --noEmit`(完整类型检查、不产产物);产物是**诊断报告**而非程序集。服务「AI 开发 vbx 脚本 / CI 编译门」工作流——拿一份 `dotnet build`-式诊断而不承担脚本副作用。

## Motivation
[motivation]: #motivation

### AI 开发 vbx 的反馈回路缺口

让 AI 迭代 `.vbx` 脚本时,需要一条「只拿诊断、不运行」的反馈通道(用户原话:类似 fxcop 和 dotnet build 的操作)。现状三条路径没有一条满足:

| 路径 | 行为 | 缺口 |
|------|------|------|
| **脚本执行**(`vbi foo.vbx`) | `RunScriptAsync` 直接 `script.RunAsync`,编译+执行一体(`CommandLineRunner.cs:201-222`) | 成功时**警告被吞**(实证:未使用变量 BC42104 的脚本打印 `RAN OK`、无警告、exit 0);错误时中止但**无法只看不跑** |
| **交互模式**(`/i`) | 每 submission `newScript.Compile()` → `DisplayDiagnostics` 再运行(`CommandLineRunner.cs:296-320`) | 只能逐提交,`DisplayDiagnostics` **截断 5 条**(`:380`),非一次性文件检查 |
| **编译模式**(`.vb` 或 `/out:` `/target:`) | `VbiCompileMode.Run` → `VisualBasicCompiler.Run`,完整 vbc 编译并**发射程序集**(`Vbi.Compile.vb:32-83`) | 会落盘程序集,不是"仅诊断";`.vbx` 无 `/out:` 不触发此模式 |

### 现成积木已存在

`Script.Compile()`(`Script.cs:231`)就是"编译不运行拿诊断"的 API:成功返回**仅警告**、失败 catch `CompilationErrorException` 返回**错误+警告**、不执行不落盘(`Script.cs:332-346` 的 `CommonCompile`)。交互模式已在用;非交互路径只差一个开关把 `RunScriptAsync` 的 `script.RunAsync` 换成"先 `Compile` 再按错误与否返回退出码"。

### 生态对照

| 工具 | 检查机制 | 命名 |
|------|---------|------|
| `tsc` | `--noEmit`(完整类型检查不发射)——**本提案语义对标** | 修饰词 |
| `cargo` / `node` / `biome` | `check`(独立动词,不产产物) | 动词 |
| `oxlint` / `eslint` | 独立 lint 工具(规则诊断) | 工具名 |
| `.NET` | **无"检查不产出"动词**(`dotnet build` 必发射)——本提案填补空白 | — |

`/diag` 是 `/diagnostics` 的短形,按"交付物"命名(AI 要的是诊断报告),不与 `/removeintchecks`(整数溢出检查,`VisualBasicCommandLineParser.vb:369`)、`/analyzer:`(分析器,`:233`)撞名。

## Detailed design
[design]: #detailed-design

### 开关与解析

- 新增 `Case "diag"` 到脚本专属分支(`VisualBasicCommandLineParser.vb:474-542` 的 `If IsScriptCommandLineParser` `Select Case name`),置新布尔字段 `diag`(仿 `optimize`,`:97` 区)。
- `VisualBasicCommandLineArguments` 新增 `Diag` 属性(仿 `InteractiveMode`,`:1549` 构造区)。
- `/diag` 是**裸开关**(无冒号),与 `/i`、`/nostdlib`、`/optimize+` 同族;在脚本分支被识别,不进 `WRN_BadSwitch`(`:1341`)。
- 模式选择不受影响:`IsCompileInvocation`(`Vbi.Compile.vb:32-59`)只认 `.vb`/`/out:`/`/target:`/`/i`,`/diag` 不触发编译模式、不强制交互,自然落入脚本模式。

### 执行分支

`CommandLineRunner.RunScriptAsync`(`CommandLineRunner.cs:201-222`)在创建 `script` 后、`script.RunAsync` 前插入:

```csharp
// /diag: 只编译拿诊断,不执行
var diagnostics = script.Compile(cancellationToken);
_compiler.ReportDiagnostics(diagnostics, _console.Error, errorLogger, compilation: null);
return diagnostics.HasAnyErrors() ? CommonCompiler.Failed : CommonCompiler.Succeeded;
```

- `script.Compile()`(`Script.cs:231` → `CommonCompile` `:332-346`):成功回仅警告、失败回错误+警告,不抛异常。
- **全量显示**:复用 `_compiler.ReportDiagnostics`(打印错误+警告),**不用**交互 `DisplayDiagnostics` 的 5 条截断(`:380`)。
- **不执行**:不调用 `script.RunAsync`,脚本副作用零发生。
- **不落盘**:`CommonCompile` 内部经 `GetExecutor` → `Builder.CreateExecutor` 会**内存 emit**(脚本引擎标准路径,`InteractiveAssemblyLoader`),但无文件写入、无进程启动。
- **退出码契约**:0 = 编译干净(可有警告),1 = 有错误(仿 vbc/dotnet build)。

### 生效面

- `vbi /diag script.vbx` → 编译诊断、不执行;exit 0/1 按上述契约。
- `vbi /diag script.vbx /warnaserror:on` → `/warnaserror:on` 是 vbc 专属开关,脚本模式报 **BC2007 警告**被忽略(实测);`/diag` 模式下**严重度配置开关暂不可用**(见 Unresolved #1)。
- 源级 `Option Strict On` 照常生效(实证:`.vbx` 内写 `Option Strict On` 使晚绑定报 BC30574、收窄报 BC30512)——`/diag` 的严格性由**文件内容**决定,`optionStrict:=OptionStrict.Off` 只是默认值(`VisualBasicScriptCompiler.vb:204`)。
- `/diag` 与 `/i` 互斥:`/diag` 优先,忽略 `/i`;`vbi /diag` 无文件时报错(见 Unresolved #2)。

### 与既有机制的关系

- **交互模式的诊断前置**已存在(`BuildAndRunAsync` `:296-320` 的 `newScript.Compile()` + `DisplayDiagnostics`),`/diag` 是把同一机制接到**非交互一次性脚本**路径并去截断、改退出码。
- **分析器不是本提案范围**:`/diag` 是编译器诊断(tsc 类比);fxcop 级规则诊断(oxlint 类比)需要 `CompilationWithAnalyzers` + 脚本路径分析器管线,独立立项。
- **`Script.Compile()` 的"仅警告"过滤**(`Script.cs:340` `Where(d => d.Severity == Warning)`)意味着 Info/Hidden 诊断被滤除——对"编译门"语义正确(Info/Hidden 不阻塞)。

## Drawbacks
[drawbacks]: #drawbacks

- **无法调严重度**:脚本模式不支持 `/warnaserror`、`/nowarn`、`/ruleset`(vbc 专属分支,脚本模式报 BC2007),`/diag` 不能把警告升级为错误或按规则过滤——对"严格编译门"是半成品。缓解:`/diag` 至少暴露全部警告(现状成功路径连警告都没有),已是净改进;严格门控留给未来分析器面。
- **内存 emit**:`CommonCompile` 内部会内存发射 submission 程序集(脚本引擎标准路径),不是字面意义的"零编译"。缓解:无落盘、无执行、无副作用,对 AI 工具链语义等价"只检查"。
- **MSYS 裸开关坑(既有问题,非新增)**:`/diag` 与 `/i`、`/nostdlib`、`/optimize+` 同为裸开关,经 git-bash/MSYS 调用会被转成 `C:/Program Files/Git/diag` → BC2001(实证:现有 `/nostdlib` 从 bash 调用同样炸)。**这是平台既有怪癖**,`/diag` 不加重;bash 工具链统一设 `MSYS2_ARG_CONV_EXCL='*'`(见 Unresolved #3)。
- **命名视觉接近 `/debug`**:`/diag` ↔ `/debug` 易打错;但 `/debug` 在脚本模式报 BC2007 警告、自纠正,无功能歧义。

## Alternatives
[alternatives]: #alternatives

| 候选 | 借鉴源 | 裁决 |
|------|--------|------|
| `/diag`(采用) | `/diagnostics` 短形,按交付物命名 | **采用**——短、零冲突、AI 消费语义明确 |
| `/norun` / `/noemit` | `tsc --noEmit` | 用户否决——按"抑制的动作"命名,可读性差 |
| `/check` | `cargo check` / `node --check` 生态标准 | 用户否决——撞 `/removeintchecks`(整数溢出) |
| `/analysis` | `clang --analyze` | 否决——误导(不含分析器),且与 `/analyzer:` 撞概念 |
| `/parseonly` | — | 否决——本模式是完整语义绑定,不止语法 |
| 什么都不做 | — | 现状:警告永远不可见于成功路径,AI 只能靠真实运行拿反馈,缺口不解决 |

**"维持现状"的代价**:warning 级问题(未使用变量、晚绑定警告等)在一次性 `.vbx` 执行中**永不可见**(实证),AI 无法用它作质量反馈。

## Unresolved questions
[unresolved]: #unresolved-questions

1. **严重度配置**:`/diag` 是否要求 `/warnaserror`/`/nowarn` 支持才能落地(否则"编译门"无法把警告变错误)?
   - 备选 A:`/diag` v1 不带严重度配置,只暴露全量诊断(最小改动)。
   - 备选 B:`/diag` 顺带把 `warnaserror`/`nowarn` 加进脚本分支(改动面 +1 组开关)。
   - 备选 C:严格门控交给未来分析器面(oxlint 类比)。
   - 建议:A(C 兜底)。
2. **互斥语义**:`vbi /diag`(无文件)与 `/diag` + `/i` 的行为?
   - 备选 A:`/diag` 需要脚本文件;无文件报错 exit 1;`/diag` 优先于 `/i`。
   - 备选 B:`/diag` 无文件时退化为交互(诊断照旧逐提交显示)。
   - 建议:A。
3. **MSYS 裸开关处理**:bash 工具链调用 `/diag` 被转路径。
   - 备选 A:文档要求 `MSYS2_ARG_CONV_EXCL='*'`(与现有 `/nostdlib` 同待遇,零代码)。
   - 备选 B:`/diag` 接受带值形式(如 `/diag:on`)规避转换,但破坏裸开关惯例。
   - 建议:A。
4. **`Script.Compile()` 的内存 emit 是否可接受**:脚本引擎标准路径、无落盘无执行。
   - 备选 A:接受(最小改动,语义等价"只检查")。
   - 备选 B:纯诊断路径绕过 `CreateExecutor`(需动 `ScriptBuilder`,改动面大)。
   - 建议:A。

## 证据来源与证据等级

- 源码(已核实,证据等级=已检查):
  - 模式分派:`Interactive\vbi\Vbi.vb:58-60`(`IsCompileInvocation` → 编译模式,否则 `RunInteractiveAsync`)、`Interactive\vbi\Vbi.Compile.vb:32-59`(`IsCompileInvocation`)。
  - 脚本解析器:`VisualBasicCommandLineParser.vb:33`(`Script` parser)、`:474-542`(脚本专属分支)、`:1341`(`WRN_BadSwitch`)、`:369`(`removeintchecks`)、`:233`(`analyzer`);`Scripting\VisualBasic\Hosting\CommandLine\Vbi.vb:18`(交互编译器用 `Script` parser)。
  - 执行路径:`CommandLineRunner.cs:201-222`(`RunScriptAsync` 直接 `RunAsync`)、`:296-320`(`BuildAndRunAsync` 交互 `Compile`+`DisplayDiagnostics`)、`:380`(`DisplayDiagnostics` 5 条截断)。
  - `Script.Compile`:`Script.cs:231`(public API)、`:332-346`(`CommonCompile`:成功仅警告、失败错误+警告)、`:144-153`(`GetCompilation`)。
  - 脚本 submission 选项:`VisualBasicScriptCompiler.vb:204`(`optionStrict:=Off` 默认)、`:194`(`CreateScriptCompilation`)。
  - 参数语义:`CommandLineParser.cs:539-545`(脚本文件后参数一律脚本参数)、`:586`(`--` 后不解析)。
- 实证(证据等级=已运行):
  - warning-only `.vbx`(未使用变量)→ 打印 `RAN OK`、无警告、exit 0(警告被吞)。
  - error `.vbx`(未声明变量)→ 报 BC30451、中止、exit 1。
  - 源级 `Option Strict On` + 晚绑定 → BC30574;+ 收窄 → BC30512(源级严格生效)。
  - 脚本模式传 `/warnaserror:on` 等 vbc 专属开关 → BC2007 警告 + 脚本照跑 exit 0。
  - 裸开关经 git-bash → BC2001(`C:/Program Files/Git/<name>`);设 `MSYS2_ARG_CONV_EXCL='*'` → 正常。
- 预测性判断(待定):`/diag` 对 `Script.Compile()` 的复用无需改 `ScriptBuilder`(Unresolved #4 备选 B 的成本为推测)。
