# 概要设计：让脚本目录链上的 `NuGet.config` 对 `#R "nuget:"` 生效

> 任务：`vbi-nuget-package-source`（依据与裁决见 `README.md`）。本文件是 F1 交付物。

## 一、问题（一句话）

`#R "nuget:Name, Ver"` 的还原把临时工程建在 `%LOCALAPPDATA%\Nukepayload2\vbi\nuget-restore\<key>`，而 **NuGet 的配置链以"工程文件所在目录"为根** ⇒ 用户放在 `.vbx` 旁边（或其仓库里）的 `NuGet.config` **完全不生效**，源只来自用户/机器级配置；且该源集合**不进缓存键**（`sourceFingerprint` 生产恒空串），换源后同 key 可能继续吃旧结果。

## 二、目标

1. 脚本目录链上的目录级 `NuGet.config` 成为真实输入（含私有源与 `packageSourceCredentials`）。
2. 源集合进入还原缓存键，恢复不变量：**同一 key ⟺ 同一组还原输入**。

## 三、方案（镜像目录层级）

不改 NuGet 的配置合并语义（`<clear/>` / `disabledPackageSources` / 凭据 / 环境变量展开 / 机器级合并全部仍由 NuGet 自己算），而是：

```
%LOCALAPPDATA%\Nukepayload2\vbi\nuget-restore\<key>\
    <镜像根相对层级>\NuGet.config        ← 从脚本目录链原样复制（可能多份，保持相对层级）
    <镜像根相对层级>\project.vbproj      ← 生成工程落在镜像深度
```

`dotnet restore <key>\<mirror>\project.vbproj` 向上走时，目录级链与真实脚本目录一致；用户/机器级由 NuGet 自然加入；**凭据随配置文件一起生效**。同时把"镜像配置文件集合"的稳定指纹喂进 `sourceFingerprint`。

**镜像根规则**：目录链上**最靠上（最外层）**的那份目录级 `NuGet.config` 的父目录；若链上没有，则镜像根 = 脚本所在目录（等价于改动前的行为）。

> 必须是**最靠上**而不是「最靠近脚本」：只有把最外层那份也纳入镜像，NuGet 从工程向上的配置链才与真实脚本目录完全一致；链上的每一份都会被收集并参与指纹。

## 四、行为对照

| 场景 | 源集合 | 缓存键 | 结果 |
| --- | --- | --- | --- |
| 脚本旁无 `NuGet.config` | 用户/机器级（改动前的行为） | 指纹 = 空（与改动前**逐字相同**的输入） | 行为零变化 |
| 脚本旁有 `NuGet.config`（含 `<clear/>` + 私有源） | 该私有源（+ 其上层目录级配置） | 指纹 = 该集合 | 用私有源还原；凭据生效 |
| 上述配置内容变更后二次运行 | 新集合 | **新 key**（新目录） | 真还原；不受 `WriteOnce` 影响 |
| 纯 REPL 输入（`filePath = ""`） | 用户/机器级 | 指纹 = 空 | v1 沿用改动前 |
| 私有源带 `packageSourceCredentials` | 同链 | 同上 | NuGet 自己处理凭据（不解析不搬运凭据语义） |

## 五、触点（四层）

| 层 | 位置 | 变化 |
| --- | --- | --- |
| 收集（新增纯函数） | `Scripting\VisualBasic\Hosting\NuGetSourceConfig.vb`（新文件） | 从脚本目录向上收集 `NuGet.config`，产出（相对镜像根路径 / 字节）列表 + 镜像根 + 失败通道；另有独立纯函数产出指纹 |
| 落盘 | `NuGetRestoreRunner.vb`（`RestoreRequest` / `DotNetRestoreRunner.WriteMirrorFiles`） | 镜像文件与工程写到镜像深度；`ProjectFilePath` 语义扩展，`obj` 落点随工程目录 |
| 接线 | `NuGetRestoreCoordinator.vb:374-390` 附近 | `sourceFingerprint` 由 `filePath` 目录链计算（ctor 显式参优先，便于测试注入）；`RestoreRequest` 带上镜像集合与工程相对路径 |
| 边界/诊断 | 同上 + `NuGetRestoreDiagnostics.vb`（VBI1012） | 空 `filePath` 规则；配置读取失败 / 镜像写入失败的新诊断 |

## 六、v1 边界（非目标）

- **不加**命令行开关（`/packagesource:` / `/configfile:` / `/packages:`）。
- **不合并**配置文件、**不解析**源语义（`<clear/>`、凭据、环境变量都不碰）。
- `#load` 链上文件的所在目录**不**参与（只认主脚本 `filePath`）。
- **不改**全局包目录（`NUGET_PACKAGES` / `~/.nuget/packages` 仍由环境决定）。
- **不改** E7 的 no-op / 字节稳定前提（靠"源进 key"，不靠"内容变了就重写"）。

## 七、风险与对策

| 风险 | 对策 |
| --- | --- |
| 镜像布局改变 `ProjectFilePath`，旧 key 目录变孤儿 | 交给既有 LRU（`StaleAfter=30d`、`Max=256`）回收；不做主动迁移 |
| 指纹不稳定（机器相关路径进入指纹） | 指纹只吃**相对镜像根的路径 + 文件字节**，不吃绝对路径/时间戳 |
| 路径大小写与跨平台（`NuGet.config` 文件名、目录枚举） | 枚举用 `OrdinalIgnoreCase`；指纹中的路径统一小写归一 |
| 长路径（镜像层级加深） | 沿用现有 key 目录深度上限；测试覆盖深嵌套脚本路径 |
| 无目录级配置时行为漂移 | 该场景指纹为空、镜像为空、工程仍在 key 根 ⇒ 与改动前逐字相同（回归用例钉死） |

## 八、验收主线

1. 无目录级配置 ⇒ 与改动前零差异（回归）。
2. 脚本旁配置生效（自动：假 runner 断言镜像与 key；人工 G：真还原 + 私有源）。
3. 同集换源 ⇒ key 变化 ⇒ 真还原。
4. 全量测试 0 失败。
