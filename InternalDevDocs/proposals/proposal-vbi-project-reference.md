# vbi 脚本/交互窗口本地工程引用：`#R "project:<工程路径>"` / Local Project References in `.vbx` scripts

> **状态：Proposed**。本文是 `#R "nuget:…"`（vbi-nuget-reference，`meetings/meeting-vbi-nuget-reference.md` RESOLUTION R1–R9，已实现于 `Scripting\`）的**同机制相邻扩展**提案：新增第三种「宿主解释的 `#R` 前缀」`project:`，引用**本地可变工程**，机制 = 调 `dotnet build` 构建出产物后**匹配合适的输出程序集**喂给脚本/REPL。与 nuget 的关键差异（restore 第三方不可变包 vs build 用户本地可变工程）是 Detailed design 的中心。**本提案一切候选均待 LDM 审视，非已裁决**。
>
> **近邻 RESOLUTION 衔接（继承 / 新增）见 Summary 末表与 Detailed design §7。**

* [x] Proposed
* [ ] Prototype: [Not Started]
* [ ] Implementation: [Not Started]
* [ ] Specification: [Not Started]

## Summary
[summary]: #summary

本提案主张：让 `.vbx` 脚本与交互窗口提交用 `#R "project:<工程路径>"` 引用**本地工程**（SDK 风格 `.vbproj`/`.csproj` 等，路径可指工程文件或含唯一工程的目录）。机制主张为——把脚本里出现的 project 引用集合交给 **dotnet CLI 构建**（引擎候选 P1，要求机器上有 .NET SDK；`dotnet build` 内部先 restore），从构建输出中**匹配合适的产物**（`bin/<配置>/<TFM>/…` 的输出程序集；多 TFM 选**运行宿主可加载**的那个；输出路径经 MSBuild 读取而非按约定猜），把「工程自身输出 + （按 v1 范围）其依赖闭包」分别喂编译引用与运行时加载。接线主张为——**沿用近邻已落地的宿主驱动环 seam**：`INuGetRestoreCoordinator` 类宿主协调器在两提交之间构建，resolver 侧新增与 `NuGetPackageResolver` 平行的 `project:` 抽象缝（宿主实现，会话作同步前端），共享 Core 单 `#R`→N（近邻 R4/U9 已实证落地）让单条 `project:` 自含主产物 + 闭包。

本特性是 `#R` 字符串操作数的**宿主/工具链解释**（与 nuget 同属近邻 R1 定性），不是 VB 语言特性；不引 VB 新语法，只新增**该字符串的解析语义**与构建/加载接线。

三条主张（均待 LDM 审视，候选与成本见 Alternatives / Unresolved）：

- **引擎（倾向候选 P1）**：调真实 `dotnet build` 构建**用户自己的工程**（不是合成临时工程；构建是 SDK 增量式的，up-to-date 时廉价）。只镜像「运行宿主 TFM」决定在多 TFM 中选哪个（沿用 R2「只镜像运行宿主、不读 `.vbx` 头部 TFM 注释」）；v1 不背任何「vbi 侧构建产物缓存/失效」状态（增量正确性归 SDK；每次出现 `project:` 都 spawn 一次，up-to-date 时近 no-op）。
- **接线（倾向候选 A1+A2a）**：协调 seam 沿用近邻形态（宿主预扫描 → 构建 → 写会话 → 编译）；resolver 侧在共享 `RuntimeMetadataReferenceResolver.ResolveReference` 加 `project:` 分支，经**并行抽象缝**（宿主 `ProjectResolver` 实现，同步只读会话）返构建产物路径数组；`project:` 与 nuget 混排各自展开 N，均走已落地的共享 Core N>1 路径。
- **门控**：只有出现 `project:` 引用才查 SDK / 构建；无任何 `#R` 前缀引用的脚本零行为、零报错；`project:` 前缀大小写不敏感（沿用 R3），近失配显式诊断（沿用 R6 宿主预扫描锚 `#R` 行）。

### 与近邻 RESOLUTION 的衔接

| nuget RESOLUTION 项 | 对本提案 | 继承/新增 |
|---|---|---|
| R1 定性：宿主/工具链契约；进产品 spec 不进 `vblang\spec\`；M5 脚本侧 | **继承**：`project:` 同为 `#R` 操作数宿主解释，VB 用户侧一致性（大小写不敏感/不静默失败/不引「第二种做法」/零回归）适用 | 继承 |
| R2 引擎 C1（dotnet CLI）；只镜像运行宿主 TFM | **继承方向、翻转机制**：C1 从「合成临时工程 restore」翻转为「用户真实工程 build」；「只镜像运行宿主 TFM」成为多 TFM **选产物的判据**（不是合成工程的输入）；不读 `.vbx` 头部声明 | 继承方向 + 新增约束 |
| R3 语法：前缀大小写不敏感 + 近失配显式诊断；共享层标注有意偏离上游 | **继承**：`project:` 前缀同规则；值文法不同（路径，无版本） | 继承 + 新增约束 |
| R4 接线：共享 Core 单 `#R`→N（补上游 TODO） | **继承**：已落地（`CommonReferenceManager.Resolution.cs`），`project:` 展开 N 直接复用；不 alter 旧 singular API 义务延续 | 继承 |
| R5 U9 spike 硬闸门 | **不适用**：N>1 已实证通过 | — |
| R6 诊断锚定契约：可读诊断在宿主预扫描、resolver 无诊断出参、metadata 子类锚 `#R` 行 | **继承**：构建错误（编译失败/MSBuild 错误/SDK 缺失）在宿主预扫描转译锚 `#R` 行 | 继承 + 新增诊断类别 |
| R7 U1-B：引擎/会话落宿主；共享 Core 留 seam + 注入位（IVT 锚 `Scripting\Core\Microsoft.CodeAnalysis.Scripting.csproj:54-58`） | **继承**：build runner + 会话落 VB 宿主层；共享层只留**并行 `project:` 抽象缝** + 注入位 | 继承 + 新增 seam |
| R8 各 U（U2-A 跨进程总调 / U3-B 浮动版本 / U4-A 版本必填 / U5 native 分层 / U6-A 仅脚本 / U8 宿主 TFM） | **继承可迁移部分**：U2 类比为「跨进程总 build、增量正确性交 SDK」；U5 native 分层沿用（net10 loader seam、net48 范围外）；U6-A 编译模式不进；U8 宿主 TFM。**不适用**：U3/U4（project 无版本）。 | 部分继承 |
| R9 Drawback 降级（C# scripting interop 注记） | **继承**：本 fork 无 C# scripting 工程；`project:` 缝同样落在 C#/VB 共享层 | 继承 |

**新增约束（nuget 没有，本项目独有）**：① `project:` 引用的是**用户本地可变工程**——nuget 的「内容寻址字节稳定缓存 + no-op restore 自验包在盘」不成立，vbi 不做自己的产物缓存/失效状态机；② 产物要**匹配**（多 TFM 选哪个、哪个配置、真实输出路径经 MSBuild 读而非约定猜）；③ 构建副作用（spawn `dotnet build`）比 no-op restore 重，需明确触发语义；④ 工程自身依赖闭包如何喂编译/运行是新增面。

## Motivation
[motivation]: #motivation

### `.vbx` 引用面现状：本机 dll、TPA/GAC 裸名、nuget 已通，本地可变工程仍靠手工

`.vbx` 的引用面目前有：本地 dll 路径与运行时平台程序集（`Samples\WpfCpuCoreInformation.vbx:1-3` 的 `PresentationCore.dll` 等，走 TPA/本机路径）、TPA 裸名（`Samples\CLAUDE.md` 的 `#R "Microsoft.WinUI"`）、以及已实现的 `#R "nuget:…"`（`Samples\SqliteNuGetDemo.vbx:19`、`Samples\AvaloniaCalculator.vbx:16-20`）。

**没有「引用一个正在迭代的本地工程」的通道。** 用户若想拿脚本直接消费自己解决方案里的一个类库（改库 → 跑脚本验证），现状只能：先手动 `dotnet build`，再抄写 `#R "bin\Debug\net10.0\MyLib.dll"`——输出路径随配置/TFM/`OutputPath` 覆盖漂移、产物易陈旧、无依赖闭包可言。`project:` 让脚本成为本地工程的**一等消费者**：编辑工程 → 重跑 `.vbx` → 自动重构建 + 引用正确产物。

### 与 nuget 对称的直觉：远程不可变包 vs 本地可变工程

用户已经接受 `#R "nuget:…"` = 「拉一个不可变第三方包」。自然的相邻期望是「引用我的本地工程」，二者共享同一宿主驱动环形态（近邻 R4/R6/R7 已铺好：协调 seam、宿主会话、共享 Core N>1）。补这一条是**同一机制族的相邻扩展**，不是另起炉灶。

### 外部先例缺位，只能自补（本机检出核对）

- dotnet/interactive（本机检出 `C:\Users\james\Projects\interactive`）：`#r` 引用形态只有 `nuget:`（`src\Microsoft.DotNet.Interactive.PackageManagement\KernelExtensions.cs:36-61`），无 `project:` 前缀；其 `src\Microsoft.DotNet.Interactive.CSharpProject\` 是另一族（workspace 工程系统，服务 try-dotnet 线路的整工程补全/编译），不是 `#r` 指令形态。
- Roslyn 上游 Scripting（本机检出 `C:\Users\james\Projects\roslyn\src\Scripting`）：`CommandLineRunner.cs` 无任何工程引用先例（grep `.csproj`/`TargetPath`/`project` 零命中）。
- 官方 file-based apps（近邻已取证）的 `#:package id@version` 是包引用，不是本地工程引用。

所以 `project:` 只能**沿自己的 nuget 先例自补**——这同时意味着它要承受与 nuget 相同的「宿主工具链契约 + SDK 前提」定位。

### 近邻把基建铺好了，边际成本主要在设计而非接线

`INuGetRestoreCoordinator` 宿主驱动环、`CommandLineRunner` 可空协调 seam（三调用点：文件脚本 `/` REPL 初提交 `/` REPL 每提交，`CommandLineRunner.cs:154-164,282-352`）、会话 + loader 装配（`VisualBasicScript.vb:170-179`）、共享 Core N>1（`CommonReferenceManager.Resolution.cs:820-848`）、loader 运行时闭包注册与 native 探测 seam——都已实现且经 279 项 `Scripting\VisualBasicTest` 锁定。`project:` 的额外成本集中在「构建/产物匹配/闭包」这一块（真实工程的语义），接线可最大化复用。

## Detailed design
[design]: #detailed-design

### 0. 机制与现状（设计输入，全部已检查/实锤）

| # | 事实 | 锚点 | 证据等级 |
|---|---|---|---|
| E1 | `#R "…"` 的值是 `StringLiteralToken` → reference directive；`ParseReferenceDirective` 于 `ParseConditional.vb:454`，仅 Script 源码合法（:463 报 `ERR_ReferenceDirectiveOnlyAllowedInScripts`）；directive 值经 `directive.File.ValueText` 可取 | `Compilers\VisualBasic\Portable\Parser\ParseConditional.vb:454-472` | 实锤（逐行读） |
| E2 | `RuntimeMetadataReferenceResolver.ResolveReference` 的分派：**先** `TryParsePackageReference`（nuget 分支，命中且 `PackageResolver` 非 null 才解析）→ **再** `IsFilePath`（dll/exe 扩展名或含目录分隔符）→ 最后 GAC/TPA 裸名。**`project:` 值现状落到 `IsFilePath`**（含 `\`/`/` 与 `.vbproj` 扩展名 → true），`PathResolver.ResolvePath` 找不到 → 空 → `ERR_MetadataFileNotFound` | `Scripting\Core\Hosting\Resolvers\RuntimeMetadataReferenceResolver.cs:145-198`；`PathUtilities.IsFilePath` 定义 `Compilers\Core\Portable\FileSystem\PathUtilities.cs:518-527` | 实锤（逐行读） |
| E3 | nuget 协调 seam：共享 `CommandLineRunner` ctor 收可选 `INuGetRestoreCoordinator`（默认 null 零行为），编译前三个调用点预扫描/还原；`PrepareCompilationAsync(code, filePath, options, ct)` 返锚定诊断或空 | `CommandLineRunner.cs:34-48,154-164,230-236,282-352`；`Scripting\Core\Hosting\CommandLine\INuGetRestoreCoordinator.cs:18-33` | 实锤 |
| E4 | 宿主装配：`VisualBasicScript.RunInteractiveAsync` 建 `NuGetPackageSession` + 共享 `InteractiveAssemblyLoader` + `CommandLineRunner`（传 resolver、`NuGetRestoreCoordinator`、loader）；编译模式（U6-A）不达此点 | `Scripting\VisualBasic\VisualBasicScript.vb:150-182` | 实锤 |
| E5 | 会话 = 同步只读前端：每包 key（canonical）→ compile 路径数组（索引 0 = 主资产）；resolver 子类只读会话；未命中空 | `Scripting\VisualBasic\Hosting\NuGetPackageSession.vb:110-191`；`NuGetPackageResolverImpl.vb:15-33` | 实锤 |
| E6 | 共享 Core N>1 已落地：单 `#R` → 逐条 push N 引用 + N 个相同 location；单值 map = 首项主资产；跨提交经 `ExplicitReferences` 全量继承；对既有 0/1 输入休眠 | `Compilers\Core\Portable\ReferenceManager\CommonReferenceManager.Resolution.cs:820-848` | 实锤（工作树 = 已应用态） |
| E7 | 还原/构建 runner 可注入 seam：`IRestoreRunner`（生产 `DotNetRestoreRunner` spawn `dotnet restore`）；决策/转译是纯函数 | `Scripting\VisualBasic\Hosting\NuGetRestoreRunner.vb:111-227`；`NuGetRestorePolicy.vb:118-137` | 实锤 |
| E8 | 宿主 TFM 镜像：`NuGetHostCapability.CreateDefault` 读宿主程序集 `TargetFrameworkAttribute`（不读 `.vbx` 头部注释）；可注入 net48 | `NuGetPackageSession.vb:99-103`（`NuGetHostCapability`） | 实锤 |
| E9 | assets 读取纯函数：`ReadAssets` 以**短 TFM 单体键**寻址 `targets` 节，取 compile（ref 优先）/runtime/native 根；compile 取整依赖闭包 | `Scripting\VisualBasic\Hosting\NuGetRestoreAssets.vb:68-141` | 实锤 |
| E10 | 本机工程形态证据：仓库自身多目标 `.vbproj`（vbi `net10.0-windows;net10.0;net48`）；`tmp/u9-fixtures\Main`+`Closure` 为 SDK 生成的 ProjectReference 链 fixture（构建日志在 `tmp/u9-fixtures\build-*.log`） | `Interactive\vbi\vbi.vbproj:7`（近邻已核）；`tmp/u9-fixtures\` | 已检查 |

推论：`project:` 值现状必然报「找不到元数据」（E2），是**全新前缀**（不破坏既有合法脚本，零回归面干净）；协调 seam（E3/E4）与 N>1（E6）与 loader 握手（E4/E5/E9）现成可复用；宿主 TFM 是选产物的判据（E8）；「构建」替代「还原」后，runner 注入 seam（E7）直接复用（换 spawn 参数），而**不能**复用内容寻址缓存（E7/E8 的缓存 key 语义对可变工程不成立）。

### 1. 语法与分类（候选主张：第三种宿主解释前缀，`project:` + 大小写不敏感）

`#R "nuget:…"` 已是「宿主解释前缀」先例；`project:` 是**第三种**（另两种为：普通文件路径走 `IsFilePath`、TPA/GAC 裸名）。解析规则（候选，镜像 E2 分派 + R3 惯例）：

1. 去首尾 `space/tab/引号`。
2. 前缀匹配 `"project:"` 用 `OrdinalIgnoreCase`（R3：VB 语言大小写不敏感；`Project:`/`PROJECT:` 合法）。
3. 去掉前缀（保留其后实际大小写与空白语义），**其余整段 = 路径**（不做逗号拆分——无版本概念；Trim 只去首尾空白，路径内部空格保留）。
4. 路径空 → 近失配（报「`project:` 后缺少路径」）。
5. 前缀近失配（`projet:`/`projec ：`/`project` 后非冒号）→ 宿主预扫描给显式诊断（R6），不静默当文件路径。
6. 后缀判据：`.vbproj`/`.csproj`（大小写不敏感）；目录（含唯一工程文件）或工程文件都接受；`.fsproj`/`.vcxproj` 等**非 VB/C# SDK 工程** v1 报清晰范围外诊断（见 §7 与 Unresolved U7）。

解析落在**共享层**：`NuGetPackageResolver.TryParsePackageReference` 是 nuget 专用（`NuGetPackageResolver.cs:23-48`）。`project:` 需要一个平行的 `TryParseProjectReference`（放哪层见 §5 接线 / U5）。

**已知副作用/歧义**：Windows 上 `:` 不能出现在文件名，`project:` 前缀无路径歧义；Linux 上 `project:foo.dll` 是合法文件名，此前若有人 `#R` 这样一个**文件相对路径**，本特性会劫持——与 nuget 前缀同理，属「前缀类新语义」可接受边界（近失配诊断覆盖 `project` 前缀误写；真实文件名撞前缀在跨平台场景需文档明示，列入 Drawbacks）。

### 2. 调用形态与可运行示例

`.vbx` 文件执行（示例为 VB 原文，带中文注释）：

```vbx
' demo-project.vbx —— 用 #R "project:…" 引用本地类库并立即使用
' project: 前缀大小写不敏感；路径可指工程文件或含唯一工程的目录；
' 相对路径以「引用它的脚本文件所在目录」为基（无脚本路径时以当前工作目录为基，与 #R 文件路径语义一致，候选规则见 U9）。

#R "project:..\SharedLibs\StringUtils\StringUtils.vbproj"

Imports StringUtils

' 假设 StringUtils 工程公开 Greeting 类型（本示例假设其公共面自带所需的全部依赖类型；
' 依赖闭包的 v1 范围见 §6 / U2）
Dim message = New Greeting().For("Ada")
Console.WriteLine(message)   ' 输出 Hello, Ada!
```

交互窗口（REPL）提交：

```
> #R "project:..\SharedLibs\StringUtils\StringUtils.vbproj"
> ? New StringUtils.Greeting().For("Ada")
Hello, Ada!
```

语法层面**无需新语法树节点**：`#R` 值本就是字符串字面量（E1），新增的是该字符串的解析语义（§1）与构建/加载接线（§3–§6）。

### 3. 架构：产物匹配语义（构建 → 匹配 → 读路径）

关键约束：**编译器绑定是同步链，resolver 无 `await` 锚点**（E2）——`dotnet build` 是异步长操作，只能放宿主协调环（与 nuget restore 同构）；resolver 保持同步只读会话。

#### 3.1 引擎候选（P1 / P2 / P3）

| 方案 | SDK | 输出正确性 | 依赖/维护 | 成本/风险 |
|---|---|---|---|---|
| **P1：`dotnet build <工程> [-c <配置>] [-f <TFM>]`（本提案倾向）** | 必需 | ★★★（MSBuild 级，SDK 增量） | 低 | 收益：与 D2（NativeAOT 桥「生成 vbproj → SDK 编译」）同族，增量正确性交 SDK、不发明状态机；成本：每次出现 `project:` spawn 一次进程；SDK 缺失路径清晰报错 |
| P2：进程内 MSBuild（`Microsoft.Build`/MSBuildLocator） | 免 spawn 自带 | ★★★ | 高 | 进程内背 MSBuild 求值/目标执行，与近邻否决 C2（进程内 NuGet.Commands 全量还原）同理由；静态链接体积/版本/许可，不取（除非 LDM 改判） |
| P3：按输出路径约定猜（`bin/<配置>/<TFM>/<AssemblyName>.dll`） | 必需 | ★★ | 中 | 被自定义 `<OutputPath>`/`<BaseOutputPath>`/`<AppendTargetFrameworkToOutputPath=false>` 打破；无法读真实 `TargetPath`；不取，除非证明 v1 覆盖面足够 |

**产物匹配 = 问 MSBuild，不按约定猜（P3 否决理由）**。真实输出路径的可靠来源是 MSBuild 属性 `TargetPath` / `TargetDir` / `TargetFileName`（随 `<OutputPath>` 等求值），候选读取手段（实现期 spike 验证，证据等级=待定）：

- `dotnet msbuild <工程> -getProperty:TargetPath`（.NET 8+/MSBuild 17.8+ 的求值查询，不触发完整构建）；或
- 构建 stdout 里 `-> C:\…\bin\Debug\net10.0\MyLib.dll` 一行的解析（`/v:m`，较脆）；或
- 读 `obj\<工程>.<ext>.nuget.dgspec.json` / `project.assets.json`（**不含**输出路径，只作闭包用，见 §6）。

#### 3.2 TFM 选择（匹配「运行宿主可加载」的产物）

沿用 R2「只镜像运行宿主 TFM」为**判据**（不是合成工程输入）：

- 工程单目标：直接 build 该 TFM。
- 工程多目标（`TargetFrameworks`）：取「运行宿主短 TFM（E8）能加载」的那个——**候选规则（待定，Unresolved U4）**：① 宿主短 TFM 精确在列 → 选它；② 否则取**不高于宿主版本的最近兼容目标**（如宿主 `net10.0`、工程列 `net8.0;netstandard2.0` → 选 `net8.0`，因 net10 运行时可加载 net8.0 目标程序集）；③ 无任何可加载目标（如宿主 net48、工程只列 net10.0）→ 清晰诊断。v1 保守子集可先只支持 ① + 显式诊断，② 留 U4 裁决。
- 平台化 TFM：宿主 `net10.0-windows` 镜像含平台版本；工程列 `net10.0-windows` / `net10.0` 时按宿主平台版本选（U8 方向：读宿主 `TargetFrameworkAttribute` 平台版本段）。
- 传给 `dotnet build` 用 `-f <选中 TFM>`（避免 build 全部目标浪费时间），候选。

#### 3.3 配置（Configuration）选择

`dotnet build` 默认 `Debug`。**v1 候选：默认 Debug，无前缀内配置语法**；`Release`/其它配置支持列为 Unresolved U3（是否允许 `#R "project:路径, Debug"` 之类第二段、或经环境/宿主级设置）。注意：选错配置会引用到错误产物，须在文档与诊断里明示「当前按 Debug 构建」。

### 4. 构建时机与「缓存失效」（nuget 与 project 的根差异）

| | nuget（已实现） | project（本提案） |
|---|---|---|
| 对象 | 第三方**不可变**包，内容寻址 | 用户本地**可变**工程 |
| 幂等键 | 精确包集 + 宿主镜像元组 → SHA-256 key → `%LOCALAPPDATA%…\nuget-restore\<key>\` 字节稳定临时工程 | **无 vbi 侧 key**：产物由用户工具链（SDK/VS）拥有；vbi 不写自己的「上次产物」状态机 |
| no-op | NuGet no-op restore 自验包在盘（近邻 E7 外部取证） | **SDK 增量 build**：`dotnet build` 的 up-to-date 检查按输入/输出时间戳，工程没变则近 no-op（只进程开销） |
| 触发策略 | 本进程同集 skip / 集合变必调 / 跨进程总调 | **候选（倾向 U2-A 类比）：每次出现 `project:` 都 spawn `dotnet build`**——SDK 自己判 up-to-date；vbi 不自担「何时工程变了」的探测。跨会话也总调。若延迟实测成问题再评估 sentinel（同 nuget U2 不引入 sentinel 的推理） |

**为什么不做 vbi 侧产物缓存/失效**：① 工程可变且可能被 VS/CLI 并行改，vbi 建「内容哈希 → 产物」缓存会与用户工具链互相打架（读到的可能不是用户刚改的源码编译出的）；② 「何时工程变了」= 文件变更探测，是 SDK 增量 build 已解决且被整个 .NET 工具链信任的问题，自写是重复发明且有正确性风险（近邻 C2/增量自解析否决同理由）；③ 唯一可靠信号 = 自己那次 `dotnet build` 的退出码 0 + 产物存在（对齐近邻「退出码 0 唯一有效信号」纪律）。代价：每次脚本运行多一次 dotnet 进程（数百毫秒级，冷 build 数秒，待实测——Unresolved U1）。

`dotnet build` 默认会先 restore（build = restore + compile），**不传 `--no-restore`**：让 SDK 自己处理工程依赖还原（近邻 C1 引擎候选正是「用真实 dotnet 绕开自写还原」，此处同理，只是换成 build）。

### 5. 接线（沿用宿主驱动环 seam，resolver 加并行缝）

#### 5.1 协调环（宿主侧，倾向 A1）

近邻共享 seam 是**单** `INuGetRestoreCoordinator?`（E3），`CommandLineRunner` ctor 只接一个。候选：

- **A1（建议）**：共享 seam 形状**不变**，由 VB 宿主装配一个**组合协调器**（`Friend`，实现 `INuGetRestoreCoordinator`，内部转发给 nuget 协调器与新建的 project 协调器——各自预扫描自己的前缀、各自决定还原/构建）。共享 `CommandLineRunner` 零改动（仍只认一个协调器）；`Scripting\Core` 不背多协调器逻辑。代价：宿主内多一层组合，属 U1-B「会话状态落宿主」同族。
- A2：把共享 seam 拓宽成 `IReadOnlyList<INuGetRestoreCoordinator>` 或加第二可选参。收益：runner 直接双协调；成本：动共享 seam + 既有 nuget 装配，diverg 面扩大，不必要。

倾向 A1。Project 协调器内部：预扫描（复用近邻 `ScanTreeForNuGetDirectives` 的遍历模式，`NuGetRestoreCoordinator.vb:258-329` 同构）→ 规范化工程请求（canonical 路径，§1）→ 判「是否已为本会话此工程构建成功」（本进程内同工程同 TFM 同配置可 skip，跨进程总调，§4）→ spawn `dotnet build`（经注入 runner，E7 seam 复用）→ 读产物与闭包 → 写会话 → 构建错误转译锚 `#R` 行（R6）。

#### 5.2 resolver 并行缝（宿主解释翻译点，倾向 A2a）

`project:` 必须在编译期翻译成产物路径（E2 分派点）。候选：

- **A2a（建议）**：在共享 `RuntimeMetadataReferenceResolver`（或经内部组合）加 `project:` 识别分支，调一个**新的并行抽象缝** `ProjectResolver`（镜像 `NuGetPackageResolver`：`Scripting\Core\Hosting\Resolvers\` 内抽象类 + 抽象 `ResolveProjectProject(path) As ImmutableArray(Of String)`），宿主实现只读会话（同步）。`ResolveReference` 对 `project:` 值返会话里该工程的产物路径数组（索引 0 = 主产物，闭包随后，对齐 E5 主资产约定）→ 走已落地的 N>1 路径（E6）。收益：源真值保留（`.vbx` 文本 = 磁盘文本，LSP 从源建编译即得同一引用集，对齐近邻 R4 否决「源内单主资产 seam」的同一理由）；与 nuget 缝完全平行，共享 Core N>1 不改。
- A2b：宿主预扫描把 `#R "project:…"` **剥行改写**成展开的 `#R "产物路径"` 再编译。成本：破坏「源真值」（近邻 R4 明确否决同形态），行号/诊断/REPL 跨提交继承都要补，不取。
- A2c：resolver 不识别 `project:`，让用户**先自己 build**、脚本只 `#R` 产物 dll（= 维持现状，Alternatives）。

倾向 A2a。`ProjectResolver` 抽象放共享层是否引入「共享 Core 背 shell 逻辑」耦合由 U5 裁决（镜像近邻 U1-B：抽象缝 + 注入位留共享，实现在宿主）。

#### 5.3 共享 Core / Scripting 改动面小结

- `RuntimeMetadataReferenceResolver.ResolveReference`：加 `project:` 分支（与 nuget 分支并列）——**共享层**本地 diverg，登记 `upstream-merge.md`（对齐近邻 R4/R7 义务）。
- `Scripting\Core\Hosting\Resolvers\`：新增 `ProjectResolver` 抽象缝 + `TryParseProjectReference`（近失配规则，§1）——共享层。
- 宿主 `Scripting\VisualBasic\Hosting\`：`ProjectRestoreCoordinator.vb`（或并入现有协调器）、`ProjectPackageSession` 扩展/新会话、`ProjectResolverImpl`、`DotNetBuildRunner`——VB 宿主层。
- 共享 Core `CommonReferenceManager.Resolution.cs` **零改动**（N>1 已在，E6）。

### 6. 运行时：产物闭包怎么喂（候选范围，重头）

nuget 的资产闭包来自 `project.assets.json` 的 compile/runtime 段（E9）。**工程 build 的产物**形态不同：

| 工程 build 后有什么 | 内容 | 是否覆盖「编译闭包/运行闭包」 |
|---|---|---|
| `bin/<配置>/<TFM>/<AssemblyName>.dll`（+ `.pdb`） | 工程自身输出 | 主产物（必须） |
| `bin/<配置>/<TFM>/<其它>.dll` | **ProjectReference 链**的输出（SDK 默认 copy-local） | 覆盖「工程引工程」的编译/运行闭包（本机可变部分） |
| `obj/project.assets.json` | 工程自身 NuGet 依赖的 compile/runtime/native 资产 | 覆盖「工程引包」的闭包（复用 E9 `ReadAssets`，target 键 = 选中的 TFM） |
| `<AssemblyName>.deps.json`（build 时生成） | 运行依赖清单 | 备选来源（见 U2-B） |
| 工程自带 `runtimes/<rid>/native`（若工程是 app/带原生） | native 资产 | 少数；多为包的 native（经 assets） |

**v1 范围候选（U2 待 LDM）**：

- **范围甲（最窄）**：只把**主产物 dll** 作编译引用 + 运行时注册；工程自身的包/工程依赖由用户**另用** `#R "nuget:…"`/`#R "project:…"` 显式补。收益：改动最小、无闭包重算；成本：工程公共面只要引用依赖类型，脚本编译即报「类型在未引用程序集」，违背「引用一个工程就该能用」的直觉，motivation 受损。
- **范围乙（建议）**：主产物 + **工程自身 ProjectReference 输出**（`bin` 目录里除主产物外的 dll，注意排除 ref 与卫星程序集）** + 工程自身 NuGet 依赖闭包**（读该工程 `obj/project.assets.json`，复用 `NuGetRestoreAssetsReader.ReadAssets`，target = §3.2 选中 TFM）。编译引用集 = 主产物 + ProjectReference 输出 + 包 compile 闭包；运行时注册 = 主产物 + ProjectReference 输出 + 包 runtime 闭包 + 包 native 根（loader seam，net10）。这使单条 `project:` 自含全图（对齐 R4 精神）。成本：多一步 assets 读取 + bin 扫描，需厘清「ProjectReference 输出 vs 包 dll」分辨（`bin` 里哪些是 copy-local 的工程引用、哪些是包——包 dll 通常**不**进库工程的 bin，故 bin 扫描基本只剩主产物 + 工程引用，天然分离，待 spike 验证）。
- **范围丙（最宽）**：解析工程 `.deps.json`/MSBuild `ReferencedProjects` 递归求全闭包。成本高，v1 不取。

**主资产约定**：resolver 返回数组索引 0 = 主产物（工程自身 `AssemblyName` 的输出 dll），其余为闭包——与近邻「单值 map = 首项主资产」（E6）完全对齐，旧 singular `GetDirectiveReference` 语义不变。

**native**：工程传递闭包若带 `runtimes/<rid>/native`（如工程依赖 sqlite），native 根经现有 loader `AddNativeProbeRoot` 下推（net10）；net48 范围外（宿主能力诊断，沿用 U5/net48 行）。工程本身若是 net10 宿主不可加载的平台（WindowsAppSDK/WinUI 工程），属宿主分发范畴，沿用近邻「范围外诊断指去宿主自带」的处理精神（U7）。

### 7. v1 范围与边界（候选）

- **范围内**：SDK 风格 `.vbproj`/`.csproj`（VB/C# 皆可，脚本消费 C# 类库是常态）；单目标与多目标（§3.2 规则）；Debug 配置默认；路径指工程文件或含唯一工程的目录；REPL 跨提交继承（复用 N>1 ExplicitReferences，E6）；构建/NU/SDK/前缀错误锚 `#R` 行（R6）。
- **范围外（显式）**：编译模式（`.vb` 批编译 / `/r:project:`，U6-A 类比——导去 `dotnet add reference + .vbproj`）；非 SDK/旧式 csproj 与 packages.config（报范围外诊断，U7）；`.fsproj`/`.vcxproj` 等非 VB/C# 工程；net48 native；WindowsAppSDK/WinUI 宿主分发类（沿用近邻口径）。
- **SDK 门控**：出现 `project:` 才查 SDK；无匹配在 `#R` 行报「需 .NET SDK ≥ X」（复用近邻 SDK 门控文案）；无 `project:` 全链路零行为（零回归测试，同近邻 §H1 golden）。
- **镜像约束**：不读 `.vbx` 头部 `' Attribute TargetFramework = "net48"` 注释（由外层 launcher 派发进程，R2 同口径）。

## Drawbacks
[drawbacks]: #drawbacks

- **进程/时间开销**：每次出现 `project:` 都 spawn 一次 `dotnet build`（数百毫秒至冷 build 数秒），比 nuget no-op restore 重；REPL 每提交都扫/判一次。
- **SDK 前提**：与 nuget 同——只有装匹配 SDK 的机器可用；商店交互宿主（终端用户机无 SDK）此功能缺失，只能清晰报错。
- **确定性/可复现性损失**：`project:` 引用的是**本地可变工程**——脚本运行结果取决于工程当前源码、配置、SDK 版本，不像 nuget 那样内容寻址可复现；同一脚本隔日可能引到不同产物。这是「引用本地工程」的固有属性，不是缺陷，但用户要明白脚本不再自含。
- **构建副作用**：脚本出现 `project:` 即触发一次真实构建（可能含错误、警告、写 `bin/obj`）；「完全只想引用 dll 不想构建」的用户应走维持现状（Alternatives）。
- **闭包复杂度**：范围乙（§6）要「bin 扫描 + assets 读取 + 分辨 ProjectReference 与包 dll」，比 nuget 的纯 assets 读取复杂，边界情况（卫星程序集、ref 程序集、copy-local 策略）易出错，v1 需 spike 实证。
- **共享层 diverg 扩大**：`project:` 分支与抽象缝落在 C#/VB 共享 `Scripting\Core`（R9 口径：本 fork 无 C# scripting 工程、无现行 C# 消费方，属潜在 interop 注记）；需登记 `upstream-merge.md`，与上游 merge 时要解。
- **前缀劫持边界**：Linux 上真实文件名以 `project:` 开头的 `#R` 相对路径会被新语义劫持（Windows 因 `:` 非法无此问题）；需文档明示。
- **范围乙的「第二真相」风险**：若把「工程依赖」也自动喂上，工程改了依赖而脚本未显式 `#R` 时，闭包来自工程自己的 assets——用户可能困惑「我脚本里没写这个包，怎么引上了」。需文档明示「`project:` 引用 = 引用该工程的完整依赖面」。

## Alternatives
[alternatives]: #alternatives

### 引擎层（P1 / P2 / P3，见 §3.1）

P1（`dotnet build` 用户真实工程，本提案倾向）相对 P2（进程内 MSBuild）/ P3（约定猜路径）的取舍见 §3.1 表。

### 语法/匹配层

| 方案 | 收益 | 成本/风险 |
|---|---|---|
| `project:` 第三前缀（本提案） | 与 nuget 前缀族对称，心智迁移顺 | 第三种「宿主解释前缀」，前缀面增多；共享层 +1 分支 |
| 目录基（`project:` 指目录） | 少写工程文件名 | 目录多工程歧义；最终仍须归一为工程文件 |
| 配置/TFM 写进前缀（`project:path, Debug` 之类） | 显式控制产物 | 文法复杂化；与 nuget「逗号=版本」心智冲突；v1 不取，留 U3 |
| 让用户先 `dotnet build` 再 `#R` 产物 dll（**维持现状，不生长枝**） | 零代码、零 SDK 门控、零闭包复杂度 | 输出路径/配置/TFM 全手工、易陈旧、无闭包；正是本提案要消除的摩擦（Motivation）——Growth 纪律要求候选含此枝，LDM 若判「手动 build 成本可接受」则本提案不成立 |

### 把本地工程当本地 NuGet 源（pack / 本地 feed）

先 `dotnet pack` 本地工程到本地 feed / 文件夹源，再走 `#R "nuget:…"`。收益：完全复用 nuget 通道，无新前缀、闭包/缓存/loader 全白拿；成本/风险：引入「每次改源码要重新 pack + 版本号」仪式，与「编辑库 → 重跑脚本」的即时迭代直觉相悖；本地 feed 源指纹/版本管理的复杂性（近邻 U3/U4 的版本语义又回来）；是另一种工作流而非直接替代。

### 闭包范围（甲 / 乙 / 丙，见 §6）

范围甲（主产物 only）最保守但违背直觉；范围丙（递归全闭包）最完整但成本高；v1 倾向范围乙（主产物 + 工程自身的 ProjectReference 输出 + NuGet 闭包）。

### 接线（A1 / A2 / A2a / A2b / A2c，见 §5）

组合协调器 A1 + 并行 resolver 缝 A2a 是相对「改共享 seam（A2）」「剥行改写（A2b，近邻已否决同形态）」「不接线靠手动 build（A2c）」的倾向。

## Unresolved questions
[unresolved]: #unresolved-questions

1. **U1 — 构建触发语义与延迟预算。** 每次出现 `project:` 都 spawn `dotnet build`（v1 倾向，SDK 增量近 no-op），还是加「本会话内同工程同 TFM 同配置 skip」（镜像 nuget 本进程同集 skip）？跨会话是否也总调？`dotnet build` up-to-date 的延迟量级（热 build vs 冷 build）需实测；是否引入「源码比产物新才 build」的 vbi 侧探测（正确性风险，见 §4 反对理由）？——候选 A（建议）：本会话内同工程同参 skip + 跨会话总调；若热 build 延迟实测成为问题再评估。证据等级：延迟数字待定（需 spike）。

2. **U2 — 工程依赖闭包的 v1 范围（§6 范围甲/乙/丙）。** 范围乙（建议：主产物 + 工程 ProjectReference 输出 + 工程自身 NuGet 闭包）的 bin 扫描分辨（哪些是 copy-local 工程引用、哪些是包 dll；ref/卫星程序集排除）需 spike 实证；范围甲是否作为「最窄可发布」保留？工程公共面引用依赖类型时范围甲必然报「类型在未引用程序集」——是否接受为 v1 限制？

3. **U3 — 配置（Configuration）选择。** v1 默认 Debug 且无前缀内配置语法，还是支持第二段（`#R "project:path, Release"`）？若支持，与 nuget「逗号 = 版本」的文法如何区分（project 无版本，逗号空置可作配置段？）——候选：v1 固定 Debug + 文档明示，配置参数化留待产品信号。

4. **U4 — 多 TFM 的「宿主可加载」判据（§3.2）。** 只支持「宿主短 TFM 精确在列」+ 否则清晰诊断，还是引入「不高于宿主版本的最近兼容目标」（需定义 net10→net8 的兼容序、netstandard 的位置、`net48` 宿主 vs `netstandard2.0` 目标）？跨主版本回退的运行时加载风险（net10 进程加载 net8.0 程序集通常可，加载 net48 程序集不可）要写清楚。

5. **U5 — `ProjectResolver` 抽象缝与 `TryParseProjectReference` 的归属。** 放共享 `Scripting\Core`（镜像 nuget 缝，未来 LSP 复用 resolver 装配）还是放 VB 宿主层（共享层不背第二前缀逻辑，但编译器 `ResolveReference` 的 `project:` 分支要在共享层认出前缀才能转调）——`project:` 分支本身必须在共享 resolver（E2 分派点），抽象缝放哪层待 LDM（对齐近邻 U1 的 A/B 权衡）。

6. **U6 — 平台化/WindowsDesktop 工程的镜像边界。** 工程列 `net10.0-windows` 且宿主为 `net10.0-windows`：镜像平台版本是否按宿主（U8 方向）；宿主为 `net10.0`（非 windows）引用 `net10.0-windows` 工程产物，编译 OK 但运行需 Windows Desktop 运行时——v1 是否报「平台不匹配」诊断？

7. **U7 — 工程类型范围与范围外诊断。** 非 SDK/旧式 `.csproj`（packages.config）、`.fsproj`/`.vcxproj`、含自定义构建目标（需特殊 props 才可 build）的工程——v1 报「范围外：仅支持 SDK 风格 VB/C# 工程」即可，还是尝试 build 后按错误转译？

8. **U8 — 产物读取手段的 spike 验证。** §3.1 的真实输出路径读取（`dotnet msbuild -getProperty:TargetPath` vs 解析 build stdout 的 `-> …dll` 行 vs 其它）需实现期 spike 实证（验证自定义 `OutputPath`/`AppendTargetFrameworkToOutputPath` 下仍正确）；未 spike 前为待定。

9. **U9 — 相对路径基准。** `#R "project:..\Lib\Lib.vbproj"` 的相对路径以「引用它的脚本文件所在目录」为基（文件脚本）还是「当前工作目录」为基（REPL/`vbi script.vbx` 由 CWD 派发）——需与现有 `#R` 文件路径（resolver `baseFilePath`）语义对齐并写清；REPL 跨提交后 CWD 变化是否影响已解析工程路径（会话里应存**绝对路径**）。

## 证据来源与证据等级

### 源码（产品自身编译器树 / Scripting / 宿主，证据等级=已检查/实锤）

- `#R` 语法面：`Compilers\VisualBasic\Portable\Parser\ParseConditional.vb:454-472`（`ParseReferenceDirective`：值取 `StringLiteralToken`、仅 Script 合法）。
- resolver 分派与现状失败路径：`Scripting\Core\Hosting\Resolvers\RuntimeMetadataReferenceResolver.cs:145-198`（`ResolveReference`：nuget 分支 :147-155、`IsFilePath` :156）；`Compilers\Core\Portable\FileSystem\PathUtilities.cs:518-527`（`IsFilePath`）。
- 共享协调 seam：`Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs:34-48`（ctor 可选参）、`:154-164`（文件脚本路径预扫描）、`:230-236`（`RestoreNuGetReferencesAsync`）、`:282-352`（REPL 初提交 + 每提交）；`Scripting\Core\Hosting\CommandLine\INuGetRestoreCoordinator.cs:18-33`。
- 宿主装配：`Scripting\VisualBasic\VisualBasicScript.vb:150-182`（session/loader/resolver/coordinator 装配；编译模式不达此点）。
- nuget 协调器预扫描模式：`Scripting\VisualBasic\Hosting\NuGetRestoreCoordinator.vb:124-329`（`PrepareCompilationAsync`/`ScanSubmission`/`ScanTreeForNuGetDirectives` 决策表）、`:336-444`（restore 触发/诊断）。
- 会话/主资产约定：`Scripting\VisualBasic\Hosting\NuGetPackageSession.vb:110-191`；`NuGetPackageResolverImpl.vb:15-33`；共享 Core 单值 map = 主资产：`Compilers\Core\Portable\ReferenceManager\CommonReferenceManager.Resolution.cs:820-848`。
- 宿主 TFM 镜像：`NuGetPackageSession.vb:99-103`（`NuGetHostCapability.CreateDefault` 读 `TargetFrameworkAttribute`）。
- runner 注入 seam / 纯函数：`Scripting\VisualBasic\Hosting\NuGetRestoreRunner.vb:111-227`；`NuGetRestorePolicy.vb:118-137`；assets 读取 `NuGetRestoreAssets.vb:68-141`；临时工程 `NuGetProjectGenerator.vb:30-84`；缓存 `NuGetRestoreCache.vb`（本项目不复用其 key 语义，只对照差异）。
- 本机工程形态：`Interactive\vbi\vbi.vbproj:7`（多目标）；`tmp/u9-fixtures\Main`+`Closure`（SDK ProjectReference 链 fixture，构建日志 `tmp/u9-fixtures\build-*.log`）。

### 生态对照（本机检出，证据等级=已检查）

- dotnet/interactive：`src\Microsoft.DotNet.Interactive.PackageManagement\KernelExtensions.cs:36-61`（`#r` 仅 nuget）；`src\Microsoft.DotNet.Interactive.CSharpProject\`（workspace 工程系统，非 `#r` 指令形态）——`project:` 前缀无先例。
- Roslyn 上游 Scripting：`src\Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs` grep `.csproj`/`TargetPath`/`project` 零命中——无工程引用先例。

### 预测性判断（证据等级=待定）

- `dotnet build` 热/冷延迟量级与 up-to-date 近 no-op 的实际表现（U1）——未实测。
- `dotnet msbuild -getProperty:TargetPath` 等输出路径读取手段在自定义 `OutputPath` 下的正确性（U8）——未 spike。
- 工程 bin 目录里「ProjectReference copy-local 输出 vs 包 dll」的天然分离假设（§6 范围乙）——未实证。
- 多 TFM「宿主可加载」兼容序规则（U4）——设计待定。
