# 手动验证清单：L2/L3 集成验证（分发 fork 编译器）

> 状态：F10 实施者产出（2026-08-23）。本清单按 test-plan.md 的 L2（打包产物）与 L3（`vbi` tool 命令面）整理，命令与预期基于**最终实际产物**（F5-F9 实现 + F7 nupkg 结构核对 + F9 功能验证），可复制粘贴执行。
> **无副作用纪律（CLAUDE.md）**：以下命令含 `dotnet pack` / 临时项目 `dotnet build` / `dotnet tool install` 等**集成副作用**（写文件、启进程、写全局工具目录）——**由用户手动跑**，不在单测/自动化范围。
> 不需要修改仓库源码即可完成全部验证；临时产物放 `tmp/exp-distribute/`（git-ignored 或验证后清理）。
> **Shell**：命令为 git bash（仓库习惯）；Windows PowerShell/cmd 用户把 `grep -i` 换成 `findstr /i`、路径 `/` 换 `\` 即可。

## 0. 前置：产物路径

| 产物 | 路径 |
|------|------|
| 编译器包（Toolset 风格） | `Installer/Toolset/bin/Release/Nukepayload2.Compilers.VBScriptDotNet.2.0.0-Beta.nupkg` |
| tool 包（vbi） | `Interactive/vbi/bin/Release/Nukepayload2.Compilers.VBScriptDotNet.Cli.2.0.0-Beta.nupkg` |

若对应 nupkg 不存在，先按 L2-1 / L3-0 重新 pack。

---

## L2 打包产物验证（编译器 NuGet 包）

### L2-1 `dotnet pack` 编译器包

```bash
cd /c/Users/james/Projects/VBScriptDotNet
dotnet pack Installer/Toolset/Nukepayload2.Compilers.VBScriptDotNet.Package.csproj -c Release -nologo
```

预期：
- EXIT 0。
- 产出 `Installer/Toolset/bin/Release/Nukepayload2.Compilers.VBScriptDotNet.2.0.0-Beta.nupkg`。
- **唯一非 NoWarn 警告 = NU5039**（包缺 README 文件，`NoWarn` 只列 NU5100;NU5128）——见「已知说明 1」，非阻断。

### L2-2 解包核对 nupkg 结构

```bash
cd /c/Users/james/Projects/VBScriptDotNet
rm -rf tmp/nupkg-check
mkdir -p tmp/nupkg-check/extracted
unzip -o Installer/Toolset/bin/Release/Nukepayload2.Compilers.VBScriptDotNet.2.0.0-Beta.nupkg -d tmp/nupkg-check/extracted
find tmp/nupkg-check/extracted -type f | sort
```

**应有（PRESENT）**：

| 项 | 作用 |
|----|------|
| `build/Nukepayload2.Compilers.VBScriptDotNet.props` | NuGet 自动导入（按包 ID 命名）；只注册 `Vbc` 任务 |
| `buildMultiTargeting/Nukepayload2.Compilers.VBScriptDotNet.props` | 多目标导入桥 |
| `tasks/netcore/Microsoft.Build.Tasks.CodeAnalysis.dll` | MSBuild `Vbc` 任务（含 Csc.cs 源码但不注册） |
| `tasks/netcore/Microsoft.CSharp.Core.targets` / `Microsoft.Managed.Core.targets` / `Microsoft.Managed.Core.CurrentVersions.targets` / `Microsoft.VisualBasic.Core.targets` | 4 个 `Microsoft.*.targets` 随任务 DLL 铺包 |
| `tasks/netcore/bincore/vbc.dll` + `vbc.deps.json` + `vbc.runtimeconfig.json` | fork vbc 三件套（`dotnet exec vbc.dll` 运行） |
| `tasks/netcore/bincore/Microsoft.CodeAnalysis.dll` + `Microsoft.CodeAnalysis.VisualBasic.dll` | Core + VisualBasic（bincore 只含 VB 面） |
| `tasks/netcore/bincore/<lang>/Microsoft.CodeAnalysis.resources.dll` + `Microsoft.CodeAnalysis.VisualBasic.resources.dll` | 卫星资源（13 语言） |

**不应有（ABSENT）**：
- `vbc.rsp` —— netcore 编译器不带 rsp（A2 已 `CopyToPublishDirectory="Never"`；`$(TargetDir)vbc.rsp` 仅留在 vbc.csproj 输出目录供命令行编译测试，不进包）。
- `Microsoft.CodeAnalysis.CSharp.dll` —— bincore 只铺 VB 面（用户定案「不含 csc」）。
- `csc` / `csi` / `VBCSCompiler` / `vbc.exe` / `*.pdb`。

### L2-3 临时 SDK 项目消费（**首要确认项：MSBuild 任务加载**）

创建临时 VB 项目 `tmp/exp-distribute/consume-vb/`：

**`consume-vb.vbproj`**：
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Nukepayload2.Compilers.VBScriptDotNet" Version="2.0.0-Beta" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

**`Program.vb`**：
```vb
Module Program
    Sub Main()
        Console.WriteLine("Hello from fork vbc")
    End Sub
End Module
```

**`nuget.config`**（本地源指向 pack 输出目录，避免污染全局源）：
```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-feed" value="C:/Users/james/Projects/VBScriptDotNet/Installer/Toolset/bin/Release" />
  </packageSources>
</configuration>
```

构建并确认任务加载：
```bash
cd /c/Users/james/Projects/VBScriptDotNet/tmp/exp-distribute/consume-vb
dotnet build -v:n
dotnet build -v:diag 2>&1 | grep -i "BuildTasks.Vbc"
```

预期：
1. `dotnet build` EXIT 0，`Hello from fork vbc` 项目编译成功（VB 面走 fork vbc）。
2. **diag 日志出现**（MSBuild 任务加载确认，对应 F7 遗留 deps.json 项）：
   ```
   Using "Microsoft.CodeAnalysis.BuildTasks.Vbc" task from assembly "…\tasks\netcore\Microsoft.Build.Tasks.CodeAnalysis.dll"
   ```
   这证明 `UsingTask AssemblyFile=$(RoslynTasksAssembly)` 生效、fork 任务 DLL 被 MSBuild 加载。
   - fork 任务内部 `GetToolDirectory()` = 任务 DLL 所在目录的 `bincore/` 子目录（`Compilers/Core/MSBuildTask/ManagedToolTask.cs:227-237`），因此 `vbc.dll` 从包内 `tasks/netcore/bincore/` 定位并 `dotnet exec` 运行——**无需 rsp、无需 tasks/netcore 下的 deps.json**。
3. `UseSharedCompilation=false`（props 注入）生效：构建日志无 compiler server 连接。

**失败模式 + fallback 修复**：
- 若出现 `MSB4062`（`"Microsoft.CodeAnalysis.BuildTasks.Vbc" 任务无法从程序集…加载`）→ MSBuild 任务加载失败。修复：在 `Installer/Toolset/Nukepayload2.Compilers.VBScriptDotNet.Package.csproj` 的 `_GetFiles` 目标补收后重打包装包再验：
  ```xml
  <_File Include="$(IntermediateOutputPath)msbuildtask-publish\Microsoft.Build.Tasks.CodeAnalysis.deps.json" TargetDir="tasks/netcore" />
  <_File Include="$(IntermediateOutputPath)msbuildtask-publish\Microsoft.Build.Tasks.CodeAnalysis.resources.dll" TargetDir="tasks/netcore" />
  ```
  注意：当前 `msbuildtask-publish/` **没有** `Microsoft.Build.Tasks.CodeAnalysis.resources.dll`（`ErrorString.resx` 用 `GenerateSource=true` 内嵌进主程序集，无卫星资源）——若该文件不存在则只需补 deps.json。

#### L2-3b：C# 项目仍走 SDK csc（不注册 Csc 任务）

创建临时 C# 项目 `tmp/exp-distribute/consume-cs/`（`Program.cs` 打印 `Hello from SDK csc`），同样的 `PackageReference`（`PrivateAssets="all"`）+ `nuget.config`：

```bash
cd /c/Users/james/Projects/VBScriptDotNet/tmp/exp-distribute/consume-cs
dotnet build -v:n
dotnet build -v:diag 2>&1 | grep -i "BuildTasks.Csc"
```

预期：
- 构建 EXIT 0。
- diag 日志中 `Csc` 任务来自 **SDK** 程序集（`…sdk/10.0.400/Roslyn/Microsoft.Build.Tasks.CodeAnalysis.dll`），**不是** fork 包内的任务 DLL——props 只注册 `Vbc`，未注册 `Csc`，C# 面不受影响。
- 注：props 注入的 `UseSharedCompilation=false` 对 C# 项目同样生效（无 compiler server），属预期、非缺陷。

### L2-4 默认 imports 生效（不依赖 rsp）

L2-3 的 `Program.vb` 用裸 `Console.WriteLine`（未显式 `Imports System`）编译通过即为证明：MSBuild 任务把 VB SDK 默认 `Imports`（`System`、`Microsoft.VisualBasic`、`System.Linq`、`System.Xml.Linq` 等）经 `Vbc.Imports` 参数转成 `/imports:` 开关传给 fork vbc（`Compilers/Core/MSBuildTask/Vbc.cs:405`），**不依赖任何 rsp 文件**。

预期：L2-3 构建成功即默认 imports 生效。可选深入：`dotnet build -v:diag` 日志的响应文件内容可见 `/imports:System` 等行。

### L2-5 net472 场景

**允许缺失，不验证为缺陷**：不打包 `tasks/net472`；VS 桌面老式（非 SDK）MSBuild 项目不在 v1 范围（用户定案）。VS SDK 项目走 MSBuild Core → bincore，不受影响。

---

## L3 `vbi` tool 命令面

### L3-0（前置）pack tool 包

```bash
cd /c/Users/james/Projects/VBScriptDotNet
dotnet pack Interactive/vbi/vbi.vbproj -c Release -nologo
```

预期：EXIT 0，产出 `Interactive/vbi/bin/Release/Nukepayload2.Compilers.VBScriptDotNet.Cli.2.0.0-Beta.nupkg`（`tools/net10.0/any/` 含 `vbi.dll` + `vbi.rsp` + 全量运行时依赖，`PackAsTool` 只发 net10.0；net10.0-windows/net48 商店宿主 TFM 独立不受影响）。

### L3-1 `dotnet tool install`

```bash
cd /c/Users/james/Projects/VBScriptDotNet
mkdir -p tmp/exp-distribute/tool-home
dotnet tool install Nukepayload2.Compilers.VBScriptDotNet.Cli \
  --version 2.0.0-Beta \
  --add-source C:/Users/james/Projects/VBScriptDotNet/Interactive/vbi/bin/Release \
  --tool-path C:/Users/james/Projects/VBScriptDotNet/tmp/exp-distribute/tool-home
```

预期：安装成功；`tmp/exp-distribute/tool-home/` 下出现 `vbi.exe`（Windows）/ `vbi`（Linux、macOS）。

后续命令统一用变量（git bash）：
```bash
vbi=C:/Users/james/Projects/VBScriptDotNet/tmp/exp-distribute/tool-home/vbi.exe
```
（Linux/macOS 把 `$vbi` 换成 tool-home 路径下的 `vbi`，或 `export PATH="/c/Users/james/Projects/VBScriptDotNet/tmp/exp-distribute/tool-home:$PATH"` 后直接用 `vbi`。Windows PowerShell 用 `$vbi = "C:\Users\james\...\tool-home\vbi.exe"`。）

### L3-2 版本双行

```bash
"$vbi" /version
```

预期（两行版本输出）：
```
Nukepayload2's fork of Visual Basic Interactive Compiler [Version 2.0.0-Beta]
Based on Roslyn [Version 5.9.0]. 
```
- 第 1 行版本 = `2.0.0-Beta`（`GetSelfVersion` 读 `Microsoft.CodeAnalysis.VisualBasic.Scripting.dll` 的 `AssemblyInformationalVersion`，B3 已把 Scripting 版本 Prefix/Suffix 覆盖为 `2.0.0`/`Beta`）。注意：SDK 默认把源修订追加进 `AssemblyInformationalVersion`，本地/Debug 构建实际显示 `2.0.0-Beta+<commit>`（如 `2.0.0-Beta+e307d0f3…`）。
- 第 2 行 = `5.9.0`（`GetRoslynVersion` 读 `Microsoft.CodeAnalysis.VisualBasic.dll` 程序集版本，Roslyn 上游版本不受影响）。
- 文案本地化：LogoLine1/2 从 13 语言 xlf 出卫星资源；zh-CN 系统显示中文 fork 文案（`Nukepayload2 的 Visual Basic 交互式编译器复刻版 [版本 {0}]` / `基于 Roslyn [版本 {0}]. `），不再显示 Microsoft 文案（旧 xlf 仍是上游 Roslyn 文案）。
- `/version` 现走 `VisualBasicInteractiveCompiler.PrintVersion`（`Vbi.vb`）输出上面两行（fork 自版本 + 基于的 Roslyn 版本）；`--version`（双横杠）不被 Roslyn 命令行 parser 支持（用户定案不修，文档统一用 `/version`）。

### L3-3 编译模式：`vbi src.vb /out:app.dll`

创建 `tmp/exp-distribute/src.vb`：
```vb
Module Program
    Sub Main()
        System.Console.WriteLine("compiled by vbi")
    End Sub
End Module
```
```bash
cd /c/Users/james/Projects/VBScriptDotNet/tmp/exp-distribute
"$vbi" src.vb /out:app.dll
dotnet app.dll
```

预期：
- 编译 EXIT 0，产出 `app.dll`（标准 .NET 程序集，引用 `System.Runtime`，非 `System.Private.CoreLib`）。
- `dotnet app.dll` 输出 `compiled by vbi`。
- 说明：`.vb` 源或 `/out:`/`/target:` 触发编译模式（`VbiCompileMode.IsCompileInvocation`）；编译模式默认注入 `/nostdlib` + 全量 .NET 引用包 + `/define:_MyType="Empty"`（见「已知说明 4」）。

### L3-4 脚本执行：`vbi script.vbx -- args`

创建 `tmp/exp-distribute/script.vbx`：
```vb
Imports System
For Each a In Args
    Console.WriteLine("arg: " & a)
Next
Console.WriteLine("hello from vbx script")
```
```bash
cd /c/Users/james/Projects/VBScriptDotNet/tmp/exp-distribute
"$vbi" script.vbx -- hello world
```

预期：
- 输出：
  ```
  arg: hello
  arg: world
  hello from vbx script
  ```
- `.vbx`（且无 `/out:`/`/target:`）走执行路径（`VisualBasicScript.RunInteractiveAsync`）；`--` 后内容作为脚本参数传给全局 `Args`。

### L3-5 shebang 解释器（Linux/macOS）

创建 `tmp/exp-distribute/shebang.vbx`：
```
#!/usr/bin/env vbi
Console.WriteLine("shebang ok")
```
```bash
cd /c/Users/james/Projects/VBScriptDotNet/tmp/exp-distribute
chmod +x shebang.vbx
export PATH="/c/Users/james/Projects/VBScriptDotNet/tmp/exp-distribute/tool-home:$PATH"
./shebang.vbx
```

预期：输出 `shebang ok`。`#!` 首行在编译器语法层已实现（`proposal-shebang-directive.md` done），`vbi` 可作解释器。

### L3-6 交互 REPL

```bash
"$vbi"
```

预期：进入 REPL，显示 Logo + 提示符；输入 `2+2` 回车 → `4`；`/exit` 或 Ctrl+Z/Ctrl+D 退出。

### L3-7 帮助文案

```bash
"$vbi" /?
```

预期：打印 Usage/Options 帮助，含 `@<file>` 响应文件说明（`The default file is vbi.rsp.`——默认 imports/references 来自随 tool 打包的 `vbi.rsp`）。

---

## 已知说明

1. **NU5039（缺 README 警告）**：`dotnet pack` 编译器包时唯一非 NoWarn 警告（`NoWarn` 现只列 NU5100;NU5128）。由用户决定是否补 README：
   - 补 `README.md` 进包：打包项目加 `<PackageReadmeFile>README.md</PackageReadmeFile>` + `<None Include="README.md" Pack="true" PackagePath="\" />`；
   - 或加 `NoWarn`：`<NoWarn>$(NoWarn);NU5039</NoWarn>`。
2. **商店版 wapproj 完整构建需 VS**：`Installer/` 商店宿主（net10.0-windows/net48）完整 wapproj 构建依赖 `DesktopBridge.props`，**dotnet CLI 缺该文件**，需用户在 **Visual Studio** 中确认 vbichooser/vbifw/vbicore 仍能构建（F9 已验证 vbicore/vbifw 独立构建通过、wapproj 引用不受 `PackAsTool` 影响）。
3. **net472 缺失（允许）**：不打包 `tasks/net472`，VS 桌面老式项目不在 v1 范围（用户定案）；VS SDK 项目走 MSBuild Core → bincore 不受影响。
4. **编译模式参考集**：`vbi src.vb /out:` 默认注入 `/nostdlib` + 全量 .NET 引用包（`packs/Microsoft.NETCore.App.Ref/*/ref/net10.0/`）+ `/define:_MyType="Empty"`。若机器只有 runtime（无引用包），回退 `/r:System.Private.CoreLib`，产物引用 SPC（仅可加载、不可被常规 .NET 项目消费）——v1 可接受，装 SDK 即正常。
5. **tool 包自带 `vbi.rsp`**（`tools/net10.0/any/vbi.rsp`）：REPL/脚本执行路径用它加载默认 imports/references；编译器包**不带** `vbc.rsp`（netcore 编译器无 rsp 需求，两者不混淆）。
