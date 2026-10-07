# 怎么测：`#Load` 不重复塞入同一个文件

（配套的判断依据和原因见同目录 `README.md`。每条测试都要断**具体的错误集合和具体数值**，不许只写"能编译过"。）

## 一、修好以后必须通过的例子

测试写在 `Scripting\VisualBasicTest\ScriptTests.vb`，用文件里已有的 `CreateScriptWithLoadDirective`（虚拟文件表，不要真的往磁盘写文件）。

| 编号 | 人话说明 | 文件内容 | 期望 |
|---|---|---|---|
| 1-1 | 两个文件共用一个公共库，库里声明了变量 | `main`：先 `#Load "a.vbx"` 再 `#Load "b.vbx"`，最后 `? z`；`a` 和 `b` 各自 `#Load "lib.vbx"`；`lib`：`Dim z As Integer = 9` | 没有任何错误；`? z` 得到 **9**。（修之前会报 `BC30260` + `BC31429`） |
| 1-2 | 同一个文件在同一个主文件里被加载两遍 | `main`：`#Load "part.vbx"` 写两遍，然后 `? 1`；`part`：`Dim q As Integer = 2` | 没有任何错误；并且 `part` 里的代码只执行**一次**（用一个会被累加的变量或计数器观察副作用，别只断"没报错"） |
| 1-3 | 公共库只打印、不声明东西 | 同 1-1 的结构，但 `lib` 里只有一行打印 | 那段打印在输出里**只出现一次**（修之前出现两次，这是已经实测过的） |

## 二、不许改坏的东西（现在就是通过的，修完还得通过）

| 编号 | 内容 | 期望 |
|---|---|---|
| 2-1 | `ScriptTests.vb:590 TestNestedLoadDirective`（`main→mid→leaf` 三层，问 `Mid()` 该得 8） | 原样通过，一行都不许改 |
| 2-2 | `ScriptTests.vb:526 TestLoadDirectiveDoesNotShiftDiagnosticSpan`（错误位置不能因为文件合并而偏） | 原样通过，一行都不许改 |
| 2-3 | **两次互不相干的运行各自加载同一个文件** | 两次都能正常加载。去重只在"同一次编译内部"生效，不能变成"整个进程只能加载一次" |
| 2-4 | 主文件里 `#Load` 写在别的语句后面 | 照旧报错（位置规则没变） |

## 三、如果连"循环加载报错不精确"一起修，需要加的例子

| 编号 | 内容 | 期望 |
|---|---|---|
| 3-1 | `ScriptTests.vb:605 TestLoadDirectiveCycleReportsAtLoadLine` | 在原有断言（只有一条错误、报在哪个文件哪一行）之上，**补一条"错误码是什么"的断言**，并加一句注释说明"这个防绕圈检查是有意设计的" |
| 3-2 | 一个文件 `#Load` 自己 | 只有一条错误、报在 `#Load` 那一行，错误码要说清是循环加载（不许再是"找不到文件"） |

## 四、真跑一遍程序核对（主线做）

用重建后的 `Interactive\vbi\bin\Debug\net10.0\vbi.exe` 跑已经留档的探针文件（在 `tmp\probes-cyc2\` 和 `tmp\probes-nest\`）：

| 探针 | 修之前跑出来的 | 修之后期望 |
|---|---|---|
| `dz.vbx`（两个文件共用带声明的库） | `BC30260` + `BC31429`，退出码 1 | 输出 `SHAREDZ-ran z=9 / A-ran / B-ran / DIAMONDZ-main z=9`，退出码 0（与 C# 一致） |
| `dup-main.vbx`（同一文件连加载两次） | `DUP-part-ran` 出现两次 | 只出现一次 |
| `a.vbx` + `b.vbx`（互相绕圈） | `b.vbx(1) : error BC2001：找不到文件“a.vbx”` | 仍然只有一条错误、仍在 `b.vbx(1)`；错误码换成能说清"循环加载"的那个（若做了第三节） |
| `tmp\probes-nest\main.vbx`（三层套娃） | `NEST-level3-leaf / NEST-level2 / NEST-level1` | **一个字都不许变**（这条是用来证明"被加载的内容先执行"这个既有语义没被动过） |

三条老坑都要注意：先 `export MSYS_NO_PATHCONV=1`、先用 `file -b` 判输出编码、循环和套娃这类形状必须写成文件（一行一行喂进 REPL 是测不出来的，因为每条提交各算一棵树）。完整实测入口见 `..\..\..\tmp\HANDOFF.md` 第 6.1 节（本机文档，不入仓）。

## 五、回归（主线做，子任务不要跑全量）

- 编译器七个测试门，失败数必须为 0；总数只允许按新增测试条数增长，跑之前先用 `git diff HEAD --numstat` 看实际改了多少，别照抄文档里的数字。
- 脚本层测试：直接运行 `Scripting\VisualBasicTest\bin\Debug\net10.0\Microsoft.CodeAnalysis.VisualBasic.Scripting.UnitTests.exe -automated`，看最后一行 JSON 里 `TestsFailed` 为 0。
- 已知干扰：有一条测试会在并行跑的时候崩在编译器自检上（另一条任务 `..\parallel-submission-binding-assert\`，登记为 issue 35）。如果这次回归撞上它，必须给出"与本次改动无关（附证据）"或"由本次改动引入（附归因链）"两种结论之一，**"再跑一次就绿了"不算结论**。

## 六、每条结论都要标是怎么验证的

- 第 1 档：单元测试能直接断言（首选，能走这档不许降级）；
- 第 2 档：必须真跑程序看输出；
- 第 3 档：只能读代码核对。
低档结论不许冒充高档交付；哪条格子三档都做不到，就回报"测不了"并说明原因，不许悄悄跳过。
