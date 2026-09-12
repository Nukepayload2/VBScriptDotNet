# Visual Basic Language Design Meeting
September 9, 2026

议题是 `proposal-vbi-project-reference`——让 `.vbx` 脚本与交互窗口用 `#R "project:<工程路径>"` 引用**本地可变工程**：把脚本里的工程引用集合交给 `dotnet build` 构建，从构建输出**匹配合适产物**（多 TFM 按「运行宿主可加载」判据选、真实输出路径经 MSBuild 读而非约定猜）喂编译/运行。触发点是 `.vbx` 的引用面虽已补上本机 dll、TPA/GAC 裸名与 `#R "nuget:…"`（`Samples\SqliteNuGetDemo.vbx:19`、`AvaloniaCalculator.vbx:16-20`），但「引用一个正在迭代的本地类库」仍只能手工 `dotnet build` 再抄 `#R "bin\Debug\net10.0\MyLib.dll"`——输出路径随配置/TFM/`OutputPath` 漂移、产物易陈旧、无依赖闭包。提案把机制主张收敛成：引擎（倾向 P1，spawn 真实 `dotnet build` 用户自己的工程）、接线（沿用近邻宿主驱动环 seam，A1 组合协调器 + resolver 侧并行 `ProjectResolver` 抽象缝 A2a）、闭包（范围乙：主产物 + 工程 ProjectReference 输出 + 工程自身 NuGet 依赖）。三条主张都标了「待 LDM 审视」。

这次会议比近邻更快收敛。两条独立路径——一条沿 VB 基因问「这设计像不像 VB、与 `#R` 前缀族和不合」，另一条沿 C# 生态问「这与 .NET 工具链合不合」——在**引擎选 P1、接线选 A1+A2a、闭包选范围乙、v1 固定 Debug** 上几乎没有来回。原因很直白：这是近邻已铺好基建（N>1 已落地、宿主驱动环已实现、loader 握手已接线）后**同一机制族的相邻扩展**，而近邻 RESOLUTION R1–R9 已把「宿主/工具链契约、非 VB 语言特性」的定性钉死。真正的会议重心落在三件近邻没有的事上：**真实工程 build 的产物匹配/闭包语义正确性、U1 触发语义的一处内在不一致、以及 REPL 长会话里可变工程重载这个提案没算到的 CLR 硬约束**。我们为此翻了几处新源码，也发现一条证据锚与工作树不符——两边独立路径各自都撞到了它，反而是会议最有价值的一个收敛信号。

## Agenda

* [Proposal: vbi 脚本/交互窗口本地工程引用 `#R "project:<工程路径>"`](#proposal-vbi-脚本交互窗口本地工程引用-r-project工程路径)

## Proposal: vbi 脚本/交互窗口本地工程引用 `#R "project:<工程路径>"`

_Related: [`../proposals/proposal-vbi-project-reference.md`](../proposals/proposal-vbi-project-reference.md)；先导/姊妹会议 `../meetings/meeting-vbi-nuget-reference.md`（RESOLUTION R1–R9，本提案继承）；`../decisions.md` M5 / D2 / D4（脚本 + 现代 .NET 双模路线的工具链对齐）；共享层合并账本 `../upstream-merge.md`（2.9–2.17 近邻已登账）_

> **来源标注**：本会议引用的源码与规范文本均逐字核对（`文件:行号`）。提案 E1–E9 锚点逐一复核属实；E10 的 `tmp\u9-fixtures\Main`+`Closure` 描述与当前工作树**不符**（两工程现均无 `<ProjectReference>`，非 ProjectReference 链，见正文）。复核中还修正了几处表述：产物读取的 stdout `->` 行解析因 MSBuild 控制台本地化而必须弃、`RuntimeMetadataReferenceResolver` 的 `Equals`/`GetHashCode` 须纳入新 `ProjectResolver` 字段、以及 U1「每次出现都 spawn」与「本会话内 skip」三处互相矛盾需收口。

### 场景与缺口

`.vbx` 脚本现在能 `#R` 本机 dll、TPA 裸名与 nuget 包，但没有「引用一个本地可变工程」的通道。用户改自己的类库 → 跑脚本验证，只能先手动 build、再抄写产物路径——而输出路径随配置/TFM/`OutputPath` 覆盖漂移，产物易陈旧，且没有依赖闭包可言。提案要补的是与 nuget 对称的直觉：`#R "nuget:…"` = 拉一个**不可变**第三方包；`project:` = **引用我的本地可变工程**，编辑工程 → 重跑 `.vbx` → 自动重构建 + 引用正确产物。外部先例缺位（dotnet/interactive 的 `#r` 只有 `nuget:`，`src\Microsoft.DotNet.Interactive.PackageManagement\KernelExtensions.cs:36-61`；Roslyn 上游 Scripting `CommandLineRunner.cs` grep `.csproj`/`TargetPath`/`project` 零命中；官方 file-based apps 是 `#:package` 包引用不是本地工程引用），所以只能沿自己的 nuget 先例自补——这同时意味着它承受与 nuget 相同的「宿主工具链契约 + SDK 前提」定位。这点我们认同，也直接引出定性的继承（见下）。

### 翻源码：E1–E9 全实、N>1 已在工作树、一条证据锚与工作树不符

我们先把提案的证据纪律核了一遍。`#R` 值取 `StringLiteralToken`、仅 Script 合法、产 `ReferenceDirectiveTrivia`（`Compilers\VisualBasic\Portable\Parser\ParseConditional.vb:454-472`，E1）；resolver 分派是 nuget 分支 → `IsFilePath` → GAC/TPA 裸名的顺序，`project:` 值现状含目录分隔符时落 `IsFilePath`、`ResolvePath` 找不到 → 空 → `ERR_MetadataFileNotFound`（`Scripting\Core\Hosting\Resolvers\RuntimeMetadataReferenceResolver.cs:145-198`；`PathUtilities.IsFilePath` 于 `Compilers\Core\Portable\FileSystem\PathUtilities.cs:518-527` 只认 `.dll`/`.exe` 扩展名或目录分隔符——E2）。协调 seam 与宿主装配属实（`CommandLineRunner.cs:34-48,230-236`；`VisualBasicScript.vb:150-182`，E3/E4）。共享 Core 的 N>1 已是**已应用态**——我们翻 `Compilers\Core\Portable\ReferenceManager\CommonReferenceManager.Resolution.cs:833-848` 时看到逐字注释：

```csharp
// A single #r directive may expand to multiple references; each is added as an
// explicit reference and anchored at the same #r directive location.
```

单值 map 保留 `boundReferences[0]` 作主资产（`:841-847`），`ResolveReferenceDirective` 返数组、`>1 throw` 已移除（`:878-902`；`upstream-merge.md` 2.9 已登账）——这正是近邻 R4 的落地形态。**`project:` 展开 N 引用直接复用这条路径，对 `Compilers\Core` 零改动。** 这是我们这次比近邻最放心的一点：近邻要动共享 ReferenceManager，本提案只是**继承**，不新增那层 diverg。

Runner 注入 seam（E7，`NuGetRestoreRunner.vb:111-227` 的 `IRestoreRunner`）、宿主 TFM 镜像（E8，`NuGetPackageSession.vb:99-103` 读 `TargetFrameworkAttribute`）、assets 读取纯函数（E9，`NuGetRestoreAssets.vb:68-141`）、vbi 多目标（E10，`Interactive\vbi\vbi.vbproj:7` `net10.0-windows;net10.0;net48`）——全部属实。

**但 E10 有一处证据与工作树不符**：提案称 `tmp\u9-fixtures\Main`+`Closure` 是「SDK 生成的 ProjectReference 链 fixture」。我们两条独立路径都去核了当前树——`Main\Main.vbproj` 与 `Closure\Closure.vbproj` **均无 `<ProjectReference>`**，grep 全目录零命中，`Main\obj\project.assets.json` 也没有 `ClosureFixture` 引用；两工程只是各自独立 build 成功的两个单工程（`build-*.log` 在）。**当前工作树里不存在一个能演示「工程引工程 copy-local 输出」的 fixture。** 这直接削弱范围乙「bin 天然分离」假设的证据基础（见下），我们把它标 Suspect，并列为计划期必须先补的缺口。

复核中还顺带发现一处实现期易漏的小坑：`RuntimeMetadataReferenceResolver` 的 `Equals`/`GetHashCode` 现把 `PackageResolver` 字段纳入（`:233-249`），若按 A2a 加平行 `ProjectResolver` 字段，这两处必须同步纳入——不是设计问题，但不写进实现方案大概率会漏。

### 定性：继承 R1——`project:` 是宿主/工具链契约，不是 VB 语言特性

`#R`/`#Load`/`#!` 都不在 VB 语言规范（`vblang\spec\preprocessing-directives.md` 只覆盖 `#Const/#If/#ExternalSource/#Region/#ExternalChecksum`）；近邻 R1 已把「`#R` 操作数的宿主解释」定性为宿主/工具链契约、进产品 spec 不进 `vblang\spec\`、映射 M5 脚本侧。`project:` 与该定性完全同构：语法树零新节点（E1），新增的只是**该字符串的解析语义**与构建/加载接线。所以：

- VB 语言层的「扩展表面区高门槛」（`vbldm-notes-2018.06.13.md:34`）**不适用**；
- 但 **VB 用户侧一致性原则恰好全部适用**——大小写不敏感、不静默失败、不引「第二种做法」、不破坏无 `project:` 的既有脚本。四条仍是量这份提案的尺子。

归档位置继承：定稿后进产品 spec（`InternalDevDocs\spec\`），不进 `vblang\spec\`。与 `decisions.md` 映射落在 M5 脚本侧；引擎 P1 与 D2（生成 vbproj → SDK 编译）和官方 file-based apps 同族。D4 不直接命中 P1 两档（不是「C# 已照顾的非底层用例」，也不是 C# interop 元数据消费面），优先级由实现规划结合路线图定——与近邻同一口径。

### 候选方案权衡：引擎——P1 对，但副作用面比 nuget restore 重

**P1（spawn 真实 `dotnet build` 用户自己的工程）——采用，两票一致，我们都给高置信。** 近邻 C1 的裁决逻辑在此几乎原样迁移：官方 file-based apps 是「`#:property` → SDK 造虚拟工程 → MSBuild/dotnet build」（`csharplang\proposals\csharp-14.0\ignored-directives.md:7-19`），本产品 D2 是「生成 vbproj → .NET SDK `PublishAot`」（`decisions.md`），P1 把可变工程的增量正确性、restore、配置/TFM 求值全交给 SDK——**不发明状态机**，正是 VB「平台已领先的，就让平台领跑、VB 对齐」的处世之道。P2（进程内 MSBuild）与近邻否决 C2 同理由——进程内背 MSBuild 求值/目标执行，静态链接/版本/许可都是坑，不取。P3（按输出路径约定猜）违背「问 MSBuild、不按约定猜」——`<OutputPath>`/`<AppendTargetFrameworkToOutputPath=false>` 一覆盖就破，属 VB 的 Option Explicit 基因排斥的隐藏假设，不取。`dotnet build` 默认先 restore，不传 `--no-restore` 正确——让 SDK 处理用户工程的包还原，正是不自写还原的延续。

**但 P1 的副作用面比 nuget restore 重，两票一致认为要在文档写透：** ① `project:` 一出现，vbi 就运行该工程的**完整 MSBuild 目标**——可能含 source generator、自定义 targets、Exec 任务甚至 pre-build 脚本；对象是用户自己的工程，信任边界可接受，但这是比 nuget（只跑还原目标）更重的一个「脚本触发的动作」。② 构建写用户工程的 `bin/obj`；工程若正被 VS/另一进程构建可能遇文件锁/竞争。③ 默认 Debug 构建而用户 IDE 可能停在 Release——「选错配置引用错产物」的风险真实存在，§3.3「当前按 Debug 构建」的明示是必要的。

C# 兼容这一路还给引擎补了一条实操脚注：**spawn 的 working directory 应设为被引工程所在目录**。`dotnet build` 的 SDK 解析（global.json）与 `Directory.Build.props`/`nuget.config` 解析都从工程文件位置向上找；近邻 `DotNetRestoreRunner` spawn 临时工程时 CWD 无碍（临时工程自带 global.json），但对用户真实工程，从 vbi 的任意 CWD spawn 会让 SDK 版本/源镜像与用户在 VS/CLI 里的预期错位。这条标 spike 确认，但方向我们认。

### 候选方案权衡：产物读取与 TFM 选择——问 MSBuild 对，stdout 解析要弃，兼容序不手搓

**产物读取（U8）：`dotnet msbuild -getProperty:TargetPath` 是正路，但我们弃了提案 §3.1 里的 stdout `->` 行解析候选。** MSBuild 控制台输出是**人类界面且本地化**——`tmp\u9-fixtures\build-*.log` 就是中文「已成功生成。/0 个警告/0 个错误」（`build-closure.log:2-4`），`->` 行的具体格式取决于 logger 与 `/v:m`，拿它当机器接口是脆的。替代回退源我们翻到一个 SDK 落盘的机器可读清单：`obj\Debug\<tfm>\<工程>.vbproj.FileListAbsolute.txt`（`tmp\u9-fixtures\Closure\obj\Debug\net10.0\Closure.vbproj.FileListAbsolute.txt:1-12`，build 后写盘，含 bin 的 dll/pdb 与 obj 的 ref/refint dll）。`-getProperty` 的一个关键疑点须 spike 实证：它是**工程求值**查询，而 SDK 风格工程里 `TargetPath` 多在 build 期 target 计算，纯求值期不一定有值；且多 TFM 工程外层无单一 `TargetPath`，必须 `-p:TargetFramework=<选中 TFM>` 强制内层。**spike 主路线 = `dotnet msbuild <proj> -p:TargetFramework=<选中> -getProperty:TargetPath`，回退 = build 后读 `FileListAbsolute.txt`，弃 stdout 解析；验收项含自定义 `OutputPath`/`AppendTargetFrameworkToOutputPath=false` 下仍正确。**

**TFM 选择（U4）：v1 我们收窄为「宿主短 TFM 精确在目标列表」+ 否则清晰诊断，规则②（不高于宿主版本的最近兼容目标）延后。** 我们担心的是「最近兼容」这张表不能手搓数值序：net10 宿主对 `net8.0;netstandard2.0` 的取舍、net48 宿主对 `netstandard2.0` 可用而对 `net8.0` 不可用——「兼容序」在不同宿主下形状不同，且随 .NET 版本演进漂移。生态里这张表已由 **NuGet.Frameworks 的 `FrameworkReducer`** 实现（NuGet 对多框架资产正是「最近兼容、向下走」），本产品 nuget 通道已隐含信任它。所以规则②若做，应镜像 NuGet 判据（或直接复用 NuGet.Frameworks），并接受自己的 spike 门控，不许「设计待定」悬空。平台部分序一并写清：宿主 `net10.0-windows` 可消费工程 `net10.0`；反向（裸 `net10.0` 宿主引用 `net10.0-windows` 工程产物）编译绑定可能过、运行需 Windows Desktop 运行时——v1 给「平台不匹配」`#R` 行诊断（U6），不让它静默到运行期。C# 兼容一路提醒：dotnet CLI 里纯 net10.0 引用 net10.0-windows 工程在 restore/nearest-TFM 阶段就会失败，vbi 编译期放行 + 运行期提示是比生态更宽容的口径，属脚本宿主特例，需文档明示。

### 候选方案权衡：闭包范围甲/乙/丙——范围乙方向对，但两个 spike 前置是硬条件

范围甲（只主产物）任何公共面暴露依赖类型的库都会让脚本编译报「类型在未引用程序集」——违背「引用一个工程就该能用」的直觉，我们不取为产品形态。范围丙（递归解析 `.deps.json`/MSBuild 递归全闭包）成本高，v1 不取。**范围乙（主产物 + 工程 ProjectReference 输出 + 工程自身 NuGet 依赖）是 v1 目标**——与 nuget 通道、也与 .NET 工程语义一致：引用工程 X 得到 X 的完整依赖面，「第二真相」风险（Drawback「我脚本里没写这个包，怎么引上了」）我们认为过度担忧——ProjectReference/包引用本就是传递闭包语义，真正的义务是**已解析引用集可观测**（失败诊断列出本次引了哪些产物/闭包；可加 verbose），不让「闭包隐藏」成为「闭包不可查」。

但范围乙有两个前置 spike，都是硬条件，且都与 E10 证据缺口挂钩：

1. **「库工程 bin 不含包 dll」的天然分离假设未实证。** 范围乙的 bin 扫描分辨「ProjectReference copy-local 输出 vs 包 dll」依赖「包 dll 通常不进库工程 bin」——而当前工作树**没有任何 ProjectReference 链 fixture 可演示**（E10 不符，见上），更没演示带 PackageReference 的库工程。C# 兼容一路的检视倾向甚至相反：SDK 的 `ResolvePackageAssets` 把 runtime 闭包喂 `ReferenceCopyLocalPaths`，库工程可能同样复制——「天然分离」很可能不成立。**必须用「带 PackageReference 的库工程」spike 实证 bin 内容；若包 dll 混入，范围乙需改问 MSBuild 图（`_ResolvedProjectReferencePaths`/每工程引用 `GetTargetPath`）而非扫目录**——这也正是 VB 基因一路的建议：闭包解析优先问 MSBuild 精确项，bin 扫描降级为兜底。
2. **资产读取与 E9 现签名不匹配。** `NuGetRestoreAssetsReader.ReadAssets` 是**按请求包 key 逐包取 compile 闭包**（`NuGetRestoreAssets.vb:93-106`，`CollectCompileClosure` 从 `requests` 指定包出发）；工程引用没有「请求包」——要的是**该工程整个 target 段的闭包**。直接「复用 ReadAssets」需要一个「读整个 target 闭包」的变体（或以工程自身名义遍历 `targets` 全部条目），不是签名级 drop-in；目标 key 用**选中 TFM**（宿主 net10、工程 net8.0 时 assets `targets` key 是 `net8.0`）——提案 §6 已写方向对，但 spike 一并验 `packageFolders`/RID 键与临时工程还原的差异（用户工程 RID 常为空）。

编译引用用 impl dll（bin）还是 ref dll：SDK 库工程默认 `ProduceReferenceAssembly`，`obj\...\ref\` 下有 ref 程序集；真实 ProjectReference 编译期吃被引工程 ref、运行期吃 impl。提案直接拿 bin（impl）又喂编译又喂运行——impl 元数据是超集通常也通，但 obj 的 ref 不进 bin（bin 只有 impl），所以这条是说明性而非阻塞；范围乙若写「排除 ref 程序集」要定义清楚排除谁。

### 候选方案权衡：接线——A1 + A2a 对，且对共享编译器 Core 零改动是本次最干净的一点

**A1（组合协调器，共享 seam 形状不变）——采用。** 共享 `CommandLineRunner` 仍只认单个 `INuGetRestoreCoordinator`（`CommandLineRunner.cs:34-48,230-236`），VB 宿主装配一个组合协调器（`Friend`，实现同一接口，内部转发给 nuget 协调器与新建的 project 协调器，各自预扫描自己的前缀、各自决定 restore/build）。共享层零改动，既有 nuget 装配零改动，diverg 面最小，与近邻 R7「引擎/会话落宿主、共享留 seam」一致。A2（把 seam 拓宽成 `IReadOnlyList` 或加第二可选参）不值得动共享 seam。一个真实的代价我们照记：`INuGetRestoreCoordinator` 将承运两类协调、名不副实——可文档标注它是通用协调 seam，或趁改动面小换名，不阻塞。

**A2a（resolver 并行缝 `ProjectResolver`，宿主实现只读会话）——采用，是唯一自洽形态。** `project:` 必须在编译期翻译成产物路径（E2 分派点是同步链），`dotnet build` 异步长操作只能放宿主协调环；resolver 保持同步只读会话与 nuget 完全同构。A2b（剥行改写）破坏源真值——近邻 R4 已明确否决同形态（「同一 `.vbx` 两套真相」），本提案继承该否决正确。A2c（维持现状，用户先 build 再 `#R` dll）正是 Motivation 要消除的摩擦，作为 Alternatives 留档即可。

共享层代价照实记：`RuntimeMetadataReferenceResolver` 加 `project:` 分支 + 并行抽象缝 + `TryParseProjectReference`，这是**第二个**落在 C#/VB 共享 Scripting 层的宿主解释前缀。interop 面：本 fork 无 C# scripting 产品，外部 C# 消费方若引用本 fork Scripting.Common 会连 `project:` 语义一起拿到——与 R9 的 nuget 注记同类，属**潜在** interop 注记、非现行回归；现行成本是 `upstream-merge.md` 又一条共享层 diverg。三条落地义务：**idle-when-null**（`ProjectResolver` 为 null 时 `project:` 值照旧走 `IsFilePath` → 现状报错，零行为变化）；在 `TryParseProjectReference`/`ProjectResolver` 文档注释与测试标注「本 fork 新增的 VB 宿主前缀，上游无此物」，防 merge 误当上游缺失回滚；登记 `upstream-merge.md`。

### 构建时机与触发语义：U1 的三处矛盾必须收口到 always-spawn

这是全场唯一一次真正来回的权衡。提案自身**三处互相矛盾**：Summary 与 §4 倾向「每次出现 `project:` 都 spawn `dotnet build`、SDK 判 up-to-date、vbi 不自担变更探测」，但 U1 候选 A（建议）与 §5.1 A1 又描述「本会话内同工程同 TFM 同配置可 skip」。两条独立路径对「v1 该不该有本会话 skip」给出了**相反的裁决**，分歧点恰好是「可变 vs 不可变」：

- VB 基因一路：**反对会话内 skip，v1 统一 always-spawn。** 论据很硬——nuget 的本进程同集 skip 安全是因为包**不可变**（同 spec → 同字节）；`project:` 工程**可变**，用户在本会话内改了库源码再提交，skip 会引用陈旧产物，正是 §4.① 自己说的「读到的可能不是用户刚改的源码编译出的」。会话内 skip 就是 §4 否定的「vbi 侧『上次产物』状态机」的微缩版。
- C# 兼容一路：**支持候选 A（本会话同参 skip + 跨会话总调）。** 论据是延迟——nuget 的 no-op restore 是同一进程内重验，`dotnet build` 每次都是新进程（MSBuild node 启动数百 ms 级），skip 把 REPL 热路径压到每提交一次。但它同时承认 skip 只在「产物未变」时安全，而「产物未变」的判定正是提案拒绝自担的。

把两路摆到一起后，我们用一个 C# 兼容一路自己挖出的新事实收口了分歧——**REPL 长会话里可变工程根本不可能热重载**：`InteractiveAssemblyLoader` 按路径与程序集 identity 记忆已加载程序集（`InteractiveAssemblyLoader.cs:50-60,463-478`，同路径已加载则返回已有实例），同路径文件内容变了但 identity（名称/版本）相同而 MVID 不同时，`ShadowCopyAndLoadDependency`/`ResolveAssembly` 的 `TryReadMvid` 对比会抛 `InteractiveAssemblyLoaderException`（`:406-422`）。CLR 本就不允许在同一 ALC 替换已加载的同 identity 程序集，`project:` 又**无版本**——nuget 靠「版本必填 + key 含版本」（U4-A/U3-B）让包升级走不同路径天然避开，project 在长会话内是**同一路径同一 identity 不同内容**。所以：

- **会话内 skip 在 REPL 里的真实作用不是「省一次重 build」，而是把「改工程需重启」这件事变得更隐蔽**——skip 后脚本会静默跑旧产物，比显式 MVID 冲突更糟（VB 一路的担忧坐实）。
- 而 always-spawn 也不解决 REPL 重载（产物变了也装不进已加载的 ALC），但至少每次提交如实问 SDK、产物与工程状态一致，MVID 冲突时给显式错误而非静默陈旧。
- 主动机场景「编辑工程 → 重跑 `.vbx`」是**文件脚本**，每次运行都是新进程，本就无重载问题；REPL 内改工程需重启会话/开新进程，是 CLR 强加的、与设计正交的已知限制。

**裁决：v1 统一 always-spawn——每次出现 `project:` 都 spawn `dotnet build`（跨提交、跨会话），删会话内 skip，正确性交 SDK 增量 up-to-date（未变工程近 no-op，只进程开销数百 ms）。** 若热 build 延迟实测在 REPL 节奏成为问题，再评估 sentinel——但任何未来 skip 必须与 SDK 的输入/输出时间戳对齐（工程输入比产物新才 build），不自担内容探测，与近邻 U2 不引入 sentinel 的推理一致。**REPL 长会话可变工程重载限制写入 Drawbacks/已知限制：改被引工程需重启会话。**

### VB 基因对照 / C# 生态对照

- **`project:` 第三前缀与前缀族和谐。** 大小写不敏感继承 R3，有规范与先例双重支撑：`lexical-grammar.md:284`「Identifiers are case insensitive…」，且指令内容层有直接先例——`LDM-2014-04-16.md:66-67` 对 `#Disable Warning` 的 `<id>`「must parse as per the same rules of VB identifiers, and are case-insensitive」并写明 C# 对应物刻意敏感。文法比 nuget 更简单（路径、无版本、无逗号拆分、Trim 只去首尾、路径内部空格保留），与「逗号 = 版本」心智不冲突。近失配诊断（`projet:`/`projec ：`/`project` 后非冒号/空路径）是「不静默失败」的收口，不是可选项。前缀劫持边界（Linux 上真文件以 `project:` 开头会被劫持）与 nuget 同类、Drawbacks 已列，可接受。唯一要补文档的行为变化：**目录引用 = 该目录当前恰好一个工程**，日后目录新增第二个工程，此前能跑的脚本会从「工作」变「报多工程歧义诊断」——不是静默失败，但脚本可用性被工程外的文件系统变化改变，须明示。
- **零回归 / 门控。** `project:` 值现状必然报「找不到元数据」（Windows 上 `:` 非法入文件名、非合法既有脚本），零回归面干净；无 `project:` 全链路零行为零报错 + 一条「无 `project:` `.vbx` 逐字节不变」回归测试钉死（对齐近邻 §H1 golden）。这条我们强推。
- **VBScript 宽松/晚绑定基因。** `project:` 不触碰绑定语义，只是把引用面扩到本地可变工程产物；对晚绑定/Option Strict Off 无冲突，对早绑定强类型消费库是主流用法。无基因摩擦。
- **C#/生态面。** P1 与官方 file-based apps、D2、近邻 C1 同族；对共享编译器 Core **零改动**、完整复用已落地的 N>1 与宿主驱动环——interop 面比近邻更干净。第二共享 Scripting 前缀按「潜在 interop 注记 + `upstream-merge.md` 登记」管理（R9 继承）。M1–M8 无新方向冲突、M8 无新元数据识别义务。**但「脚本不再自含」的边界要写透**：nuget 已让脚本依赖 restore，`project:` 让脚本依赖**构建时刻的工作树**（`Samples\CLAUDE.md:9`「No project files or compilation required」再推远一步）；产品 spec 定稿时把「`project:` 引用的不是固定产物、是构建时刻的工程状态」作为首要用户须知。

### RESOLUTION:

1. **定性**：`project:` 是**宿主/工具链契约**、第三种宿主解释前缀，不是 VB 语言特性——继承近邻 R1（语言层「扩展表面区高门槛」不适用；VB 用户侧一致性——大小写不敏感/不静默失败/不引第二种做法/零回归——适用）。定稿后进产品 spec（`InternalDevDocs\spec\`），不进 `vblang\spec\`。映射 `decisions.md` M5 脚本侧；引擎 P1 与 D2/官方 file-based apps 同族、与 C# 生态同向。D4 不直接命中 P1 两档，优先级由实现规划结合路线图定。
2. **语法**：`project:` 前缀大小写不敏感（`OrdinalIgnoreCase`，继承 R3），剥前缀后**其余整段 = 路径**（无版本、无逗号拆分、Trim 只去首尾、路径内部空格保留）；空路径与近失配（`projet:`/`projec ：`/`project` 后非冒号）在宿主预扫描给锚 `#R` 行的显式诊断。目录可作引用目标但**仅在当前恰好含一个工程文件时**；多工程 → 显式歧义诊断。Windows `:` 无路径歧义；Linux 前缀劫持边界与「目录日后新增工程会改变脚本可用性」写入文档。
3. **引擎：采用 P1**（spawn 真实 `dotnet build` 构建用户自己的工程；内含 restore、SDK 增量 up-to-date）。P2（进程内 MSBuild）/ P3（按约定猜输出路径）否决（理由留档）。**spawn 的 WorkingDirectory 设为被引工程所在目录**（SDK/`global.json`/`nuget.config` 解析与用户工具链一致）。SDK 门控：出现 `project:` 才查 SDK；无匹配在 `#R` 行报清晰错误；**无 `project:` 全链路零行为零报错 + 一条「逐字节不变」回归测试**。副作用面写透：`project:` 引用 = 触发该工程一次真实构建（含其全部构建逻辑与 `bin/obj` 写入），比 nuget restore 重。
4. **构建触发（U1）——v1 统一 always-spawn**：每次出现 `project:` 都 spawn `dotnet build`（跨提交、跨会话），**删「本会话内同工程同参 skip」候选**——可变工程使 skip 成为 vbi 侧陈旧状态机（§4 反对理由）；SDK up-to-date 对未变工程近 no-op。若 REPL 热路径延迟实测成为问题再评估 sentinel，但任何未来 skip 必须与 SDK 输入/输出时间戳对齐、不自担内容探测。**已知限制（新）：REPL 长会话内修改被引工程 → 需重启会话/新进程生效**（CLR 同一 ALC 不可替换同 identity 程序集，`InteractiveAssemblyLoader.cs:406-422` MVID 冲突路径）；文件脚本（每跑新进程）无此问题——写入 Drawbacks/产品文档。
5. **产物读取（U8）——弃 stdout `->` 行解析**（MSBuild 控制台是本地化人类界面；`build-*.log` 中文本地化铁证）。主路线 = `dotnet msbuild <proj> -p:TargetFramework=<选中 TFM> -getProperty:TargetPath`（`-getProperty` 纯求值期对 `TargetPath` 是否可得、多 TFM 内层行为 = spike 验收项）；回退 = build 后读 SDK 落盘的 `<工程>.vbproj.FileListAbsolute.txt`。闭包读取优先问 MSBuild 精确项（`_ResolvedProjectReferencePaths`/`TargetPath`），bin 扫描降级为兜底。验收覆盖自定义 `OutputPath`/`AppendTargetFrameworkToOutputPath=false` 下仍正确。
6. **TFM 选择（U4/U6）——v1 收窄**：只支持「宿主短 TFM 精确在目标列表」+ 否则清晰诊断；规则②（不高于宿主的最近兼容目标）**延后**，若做须镜像 NuGet 框架选择语义（`FrameworkReducer`/NuGet.Frameworks 判据或 spike 实证自写表一致），**不手搓数值兼容表**。平台部分序写清：宿主 `net10.0-windows` 可消费工程 `net10.0`；裸 `net10.0` 宿主引用 `net10.0-windows` 工程产物 → 编译期放行 + 「运行需 Windows Desktop 运行时」`#R` 行提示（脚本宿主特例，比 dotnet CLI 更宽容，文档明示）。
7. **闭包（U2）——范围乙为 v1 目标，两个 spike 前置是硬条件**：① 用**带 PackageReference 的库工程** spike 实证「库工程 bin 是否含包 dll」——当前工作树**无 ProjectReference 链 fixture 可演示**（E10 证据订正，见 #12）；若包 dll 混入，闭包改问 MSBuild 图而非扫目录。② 资产读取需「读整个 target 闭包」**变体**，非 E9 `ReadAssets` 按包 key 的签名级 drop-in；目标 key = 选中 TFM。已解析引用集**可观测**（失败诊断列出闭包；可选 verbose）。范围甲不取为产品形态（真实库上立即失败；仅可作分阶段内部临时形态）；范围丙 v1 不取。
8. **接线——采用 A1 + A2a**：宿主内组合协调器（共享 seam 形状不变、零改动、既有 nuget 装配不变；`INuGetRestoreCoordinator` 名不副实 → 文档标注为通用协调 seam，换名可选不阻塞）；共享 resolver 加 `project:` 分支 + 并行 `ProjectResolver` 抽象缝 + `TryParseProjectReference`（放共享 `Scripting\Core`、实现在 VB 宿主，镜像近邻 U1-B），只读会话。**对 `Compilers\Core` 零改动**（继承已落地 N>1，`CommonReferenceManager.Resolution.cs:833-848`；不 alter 旧 singular API）。A2b（剥行改写）/A2（拓宽 seam）否决；A2c（维持现状）留档。A2a 三条义务：idle-when-null（`ProjectResolver` null 时 `project:` 值照旧走现状报错）；共享层文档注释 + 测试标注「本 fork 新增 VB 宿主前缀、上游无此物」防 merge 误回滚；登记 `upstream-merge.md`。**实现细节：`RuntimeMetadataReferenceResolver` 的 `Equals`/`GetHashCode`（`:233-249`）须纳入新 `ProjectResolver` 字段。**
9. **配置（U3）**：v1 固定 Debug + 文档明示「当前按 Debug 构建」；无前缀内配置语法（与 nuget「逗号 = 版本」心智冲突）。
10. **工程类型范围（U7）**：宿主预扫描**先判工程文件类型**；非 SDK/旧式 csproj/packages.config/`.fsproj`/`.vcxproj` 在 build 前报「范围外：仅支持 SDK 风格 VB/C# 工程」清晰诊断，不「先 build 再转译」MSBuild 噪音。
11. **相对路径（U9）**：文件脚本以「引用它的脚本文件所在目录」为基（resolver `baseFilePath` = 树文件路径）；REPL/`vbi script.vbx` 派发以 CWD 为基（对齐现有 `#R` 文件路径语义）；**会话里存绝对 canonical 路径**，防 REPL 跨提交 CWD 漂移。
12. **E10 证据订正**：`tmp\u9-fixtures\Main`+`Closure` 当前**不是** ProjectReference 链（两 `.vbproj` 均无 `<ProjectReference>`，`Main\obj\project.assets.json` 无 `ClosureFixture` 引用）——提案证据来源节须改述或换真链 fixture；计划期补「带 ProjectReference 链」+「带 PackageReference 的库工程」两个 fixture 支撑范围乙 spike。本 RESOLUTION 为该表述的 supersede 口径。

### Implication:

- **能力面**：`.vbx` 从「可 `#R` 本机 dll / TPA / nuget」到「可 `#R "project:…"` 引用本地可变工程」，编辑工程 → 重跑 `.vbx` → 自动重构建 + 引用正确产物 + 工程依赖闭包。主场景（文件脚本）端到端成立；REPL 内改工程需重启会话（CLR 不可替换已加载同 identity 程序集）。
- **架构面**：「构建 / 产物匹配 / 闭包」是真实工程语义，比 nuget 的「restore / assets 读取」重一步——但增量正确性交 SDK、TFM 兼容序镜像 NuGet、闭包读 MSBuild 精确项，三条「不发明机器」守住了近邻的架构哲学。共享 `Scripting\Core` +1 前缀 +1 抽象缝 +1 diverg（登记账本）；共享编译器 Core 零改动，是本次 interop 最干净的点。
- **生态面**：P1 与官方 file-based apps / D2 / 近邻 C1 同族；`project:` 对 C# 生态零 breaking（N>1 已落地、旧 singular API 不动）；第二个共享 Scripting 前缀属**潜在** interop 注记（本 fork 无 C# 脚本产品）。
- **诊断面**：构建错误（编译失败/MSBuild 错误/SDK 缺失）由宿主预扫描转译锚 `#R` 行（继承 R6）；resolver 空回只给通用锚定。新增诊断类别：SDK 缺失 / 构建失败 / 范围外工程类型 / 平台不匹配 / 多工程目录歧义。
- **回归面**：无 `project:` 全链路零行为零报错 + 逐字节回归测试；`ProjectResolver` idle-when-null 保证共享 resolver 零行为变化。
- **文档面**：`project:` 引用的不是固定产物、是**构建时刻的工程状态**；当前按 Debug 构建；目录引用 = 目录当前恰好一个工程；REPL 内改工程需重启。这些是「脚本不再自含」边界的首要用户须知。

### OPEN QUESTIONS / TODO / Follow-up

- `OPEN QUESTION`（计划期 spike 硬门控，本会议不开实现）：U8 产物读取主路线实证（`-getProperty:TargetPath` 求值期可得性 / 多 TFM 内层 / 自定义 `OutputPath` 下正确性；回退 `FileListAbsolute.txt`）。
- `OPEN QUESTION`（计划期 spike 硬门控）：范围乙闭包实证——带 PackageReference 的库工程 bin 是否含包 dll（决定 bin 扫描 vs MSBuild 图）；「读整个 target 闭包」assets 变体；`packageFolders`/RID 键差异。
- `OPEN QUESTION`：U4 规则②（最近兼容）若将来放开——镜像 NuGet.Frameworks `FrameworkReducer` 判据的 spike 与单测锁定；net10 进程加载 net8.0/netstandard2.0 可、net48 程序集不可的运行期规则写清。
- `OPEN QUESTION`：`dotnet build` 热/冷 spawn 延迟量级（U1 测量）——若 REPL 热路径需 sentinel，其失效语义必须与 SDK 输入/输出时间戳对齐。
- `TODO`：补「带 ProjectReference 链」+「带 PackageReference 的库工程」fixture，替代当前不符的 `u9-fixtures` 描述（E10）。
- `TODO`：范围乙失败诊断列出已解析引用集（可观测义务）；`RuntimeMetadataReferenceResolver.Equals/GetHashCode` 纳入 `ProjectResolver`。
- `Follow-up`：A1 使 `INuGetRestoreCoordinator` 名不副实——文档标注通用协调 seam，或趁改动面小换名。
- `Suspect`（外部工具链，spike 确认）：`-getProperty` 对 `TargetPath` 的纯求值期可得性；SDK 库工程默认是否复制包 runtime 闭包到 bin；`dotnet build` 的 global.json/Directory.Build.props 解析起点。

### 状态

- **LDM 状态**：**Active**。
- **三态判定：Active**——方向（P1 + A1/A2a + 范围乙 + always-spawn + 范围/配置收窄）两票独立收敛且高置信；定性继承近邻 R1 无悬念（宿主/工具链契约、M5、进产品 spec）；证据纪律上乘，提案 E1–E9 逐一复核属实，N>1 已在工作树（`CommonReferenceManager.Resolution.cs:833-848`）使 `Compilers\Core` 零改动、interop 面比近邻更干净；VB 基因（大小写不敏感有 `LDM-2014-04-16.md:66-67` 指令内容先例、零回归、不静默失败）与 C# 生态对齐（P1 = 真实 dotnet build 同族）全数守住。会议裁定的修正集中在：U1 三处矛盾收口到 always-spawn + REPL 重载已知限制（新 Drawback）、U8 弃 stdout 解析改 `-getProperty`/`FileListAbsolute.txt`、U4 v1 收窄为精确在列、范围乙两 spike 前置、E10 证据订正——均为计划期可交付的收口，不构成设计阻塞。**剩余未定全部是 spike 交付物（产物读取 / 闭包 bin 实证 / 兼容序若放开 / 延迟测量）与 fixture 补齐。** 下一阶段是任务计划（`tasks\<slug>\`），其第一道硬门控是 U8 + 范围乙两个 spike；本 RESOLUTION 不开启实现，实现以 spike 与任务计划通过为准。
- **独立五维评审为不入库工作材料**（`tmp\meetings\vbi-project-reference\evaluation.md`，git-ignored），仅供主持人/规划内部使用，不混入官方会议记录。
