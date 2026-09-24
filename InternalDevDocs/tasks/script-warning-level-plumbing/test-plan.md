# 测试计划：`ScriptOptions.WarningLevel` 转发（script-warning-level-plumbing）

档位标注：**档 1**＝单元测试读数；**档 3**＝读码。收口判据只用档 1。

## 一、编译层（`Compilers\VisualBasicSemanticTest\Compilation\VisualBasicCompilationOptionsTests.vb`）

| 格 | 断言 | 覆盖点 |
|---|---|---|
| `WithWarningLevel` 默认 | `New VisualBasicCompilationOptions(ConsoleApplication).WarningLevel = 1` | 非脚本默认不翻转（免疫正证） |
| 置位 | `options.WithWarningLevel(0).WarningLevel = 0` 且 `options.WarningLevel` 仍 1 | 不可变、不动源 |
| 相等短路 | `Assert.Same(options, options.WithWarningLevel(1))` | 与 C# 同形的 identity 守卫 |
| 值语义 | `WithWarningLevel(0) ≠ options`；`WithWarningLevel(0) = WithWarningLevel(0)` | `Equals`/`GetHashCode` 纳入 WarningLevel |
| 拷贝保留 | `New VisualBasicCompilationOptions(optionZero).WarningLevel = 0` | 拷贝构造器修复 |
| 链式组合 | `options.WithWarningLevel(0).WithRootNamespace("A.B").WarningLevel = 0` | `With*` 不再互相抹掉级别 |

## 二、脚本层（`Scripting\VisualBasicTest\ScriptOptionsTests.vb`）

| 格 | 断言 | 覆盖点 |
|---|---|---|
| `WarningLevel_ReachesTheCompilationOption`（原 `…DoesNotReach…`，D5 钉桩回收） | `Create(…, WithWarningLevel(0)).GetCompilation().Options.WarningLevel = 0`；`(3)`→3；`ScriptOptions.Default`→4 | 转发抵达编译对象；默认 4（C# 同） |
| 同格保留的对照 | `OptimizationLevel.Release`、`CheckOverflow(True)` 仍抵达 | 与相邻选项族同构，非空断言 |

> L2 格数不变（仅改名+改断言）。Semantic 层 +1 格 ⇒ 门 5869→5870（Passed 5765→5766）。

## 三、回收既有分叉钉桩

- `ScriptOptionsTests.WarningLevel_DoesNotReachTheCompilationOption`：原把"编译对象恒 1"钉成期望（D5 分叉）。修复即分叉消失 ⇒ 改写为 `WarningLevel_ReachesTheCompilationOption`，期望改 0/3/4。
- `ScriptModeApiSurfaceConformanceTests.vb` 文档串：把"#14 WarningLevel … registered divergence"改为"已 C# parity、仅剩 AllowUnsafe 不适用"。
- `issues\issue-warning-level-not-plumbed.md` → Fixed；`issues\README.md` 第 27 行状态同步。

## 四、全量回归

- 七门：仅 Semantic 期望值 +1；其余六门数字一字不动（含 `VisualBasicCompilationOptions.vb` 属上游同名，但普通编译默认 1 未变 ⇒ 非脚本用例零红）。
- L2 `-automated`：769/0。
- 非脚本免疫佐证：除新格外，编译器既有门数字不变。

## 五、构建面与提交纪律

- 独占构建面；子代理禁跑全量。
- **不** `git add` / `git commit`；commit 号不预填。
