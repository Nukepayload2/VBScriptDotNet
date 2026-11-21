# Scripting：net10 下 XML LINQ 引用缺失，每个提交报 24 条 BC30002（已修复）

- **状态**：**Fixed**（`bdb7735`，2026-08-22）
- **发现场景**：`Scripting` 测试在 net10 上 63 项失败
- **影响面**：所有脚本提交（`.vbx`、REPL 与嵌入宿主共用同一条提交路径）

## 症状

每个脚本提交都报 24 条 `BC30002`（未定义类型或命名空间），条数固定，与该脚本自己写了什么无关。普通编译（`vbc` 编译项目）不受影响。

## 根因

net10 把 `System.Xml.Linq` 的实现移进了 `System.Private.Xml.Linq`，`System.Xml.Linq.dll` 只剩转发。脚本编译器会在提交里嵌入一份 internal XML helper 树（`IncludeInternalXmlHelper`），该树的引用点需要 XML LINQ 程序集在引用集合里；而提交的引用集合按名字只覆盖了 `System.Xml.Linq`，绑定期取不到该程序集，于是 helper 树里的每个引用点各报一条 `BC30002`。

## 修法

`Scripting\VisualBasic\VisualBasicScriptCompiler.vb` 增加一条按运行时目录探测的引用（`CreateXmlLinqReference`，`:33-47`）：

- 以 `GetType(Object).Assembly.Location` 的目录为根，依次尝试 `System.Private.Xml.Linq.dll`、`System.Xml.Linq.dll`；
- 命中即 `MetadataReference.CreateFromFile`，两者都不在则返回 `Nothing`；
- 探测结果缓存在 `s_xmlLinqReference`，并入每个提交的引用集合。

**为什么不写死文件名**：两个名字并非处处存在——net10 上是前者，net472 上是后者。无条件的 `CreateFromFile` 会让该类型的静态初始化器在桌面宿主上直接失败。探测失败时返回 `Nothing`，让缺少 XML LINQ 的宿主退化为普通编译错误，而不是宿主启动即崩。

## 验证

- 修复前该组 63 项失败；修复后转绿（`bdb7735`）。
- 回归：脚本提交路径与普通编译路径的引用集合仍各自独立，普通编译未受影响。

## 边界

- 该探测依赖 `Assembly.Location`。在 Blazor WebAssembly 等没有文件系统路径的宿主上 `Location` 为空，本条探测不成立，XML LINQ 引用需要由宿主以字节形式注入（见 `..\proposals\proposal-blazor-wasm-repl.md` 的宿主注入方案）。
