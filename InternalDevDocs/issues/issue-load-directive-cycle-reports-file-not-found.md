# issue 34：`#Load` 的"已加载/循环加载"保护把**存在的文件**报成 `BC2001` 找不到文件

- **登记日期**：2026-09-24
- **状态**：**Fixed**（已验证，commit 待作者提交后补；走 **once 语义**，A／B 两半一并解决，账本 `..\upstream-merge.md` §2.25(j)，队列 #12，规范 `..\spec\spec-load-directive.md` 中英两份，`..\..\decisions.md` **D9**）；**计划已建**：`..\tasks\load-directive-dedup\{README,test-plan}.md`（待开工，F01 未派）
- **性质**：**诊断错位**（消息指向不存在的原因）＋ **未规定的方言行为**（`#Load` 成环/重加载时到底该不该执行，spec 没有任何条款）
- **不是**：崩溃、非法输入被拒 —— 文件确实存在且同一轮里刚被成功打开过

## 一、症状与复现条件（已运行，档 2：重建后的 Debug `vbi`）

两档 `cwd` 都试过硬的读数：**与 `cwd` 无关**（`cwd=文件目录` 与 `cwd=vbi bin 目录` 结果逐字相同）。

```
probes-cyc2/a.vbx :  #Load "b.vbx"  + Console.WriteLine("CYC-a")
probes-cyc2/b.vbx :  #Load "a.vbx"  + Console.WriteLine("CYC-b")     ' 互相引用成环
```

```
b.vbx(1) : error BC2001: 找不到文件“a.vbx”
#Load "a.vbx"
      ~~~~~~~
```

自载格（主文件 `self.vbx` 加载 `s.vbx`，而 `s.vbx` 又加载自己）同样：

```
s.vbx(1) : error BC2001: 找不到文件“s.vbx”
```

**文件存在的实锤**：`ls -l` 显示 `a.vbx`／`b.vbx` 各 56 字节；且 `a.vbx` 的 `#Load "b.vbx"` 这一层**先成功了**（报错发生在 `b.vbx` 里回指 `a.vbx` 那一行）⇒ 不是路径解析失败，是"这轮已经打开过"被当成了"找不到"。

## 二、对照：三级嵌套加载是好的（⇒ 不是"嵌套层丢失路径上下文"）

`main.vbx → mid.vbx → leaf.vbx` 三级，`cwd=/tmp`（远离文件目录），绝对路径只给主文件：

```
NEST-level3-leaf
NEST-level2
NEST-level1
```

⇒ 二、三级 `#Load` 的相对路径解析正常，且**加载内容插在被加载的位置**（深度优先、源码序）。本条只在"重加载/成环"这一格出错。

## 三、C# 侧对照（已运行，`csi 5.10.0-1.26380.3`）

同形状（`#load` 互相引用）在 C# **不报任何诊断**，exit 0，且内容执行了两次：

```
CYC-a
CYC-b
CYC-a
```

⇒ 两侧行为与诊断都不同：C# 允许（并重复执行），本 fork 拒绝但把拒绝说成"文件找不到"。

## 四、既有规格覆盖情况

`spec\spec-scripting-dialect.md` 与 `zh-CN` 里 `#Load` 只有一条错误码条目（`BC36967 ERR_LoadDirectiveOnlyAllowedInScripts`，普通编译不许用），另有加载位置规则的对照（VB `BC37002` ↔ C# `CS8098`「第一个令牌之后不得用 `#Load`」——**这一条两侧同形，实测已确认**）。**"成环/重加载"没有任何条款** ⇒ 本条同时是规格缺口。

## 五、读码与既有测试之后的改判（实锤）

原判"这是个未登记缺陷"**一半下错了**，读实现与既有测试后更正：

- **成环保护是有意的既有设计**：`Scripting\VisualBasic\VisualBasicScriptCompiler.vb:187-198` 用 `activeLoads As HashSet(Of String)`（**祖先栈**，不是全局已加载集）＋ `CollectLoadTrees(...)` 返回诊断 → `ThrowLoadDirectiveError`。
- **且已有测试钉住**：`Scripting\VisualBasicTest\ScriptTests.vb:605` `TestLoadDirectiveCycleReportsAtLoadLine` 断 `ex.Diagnostics.Single()`、路径＝`mid.vbx`、行＝1，注释逐字「The cycle is detected at mid.vbx's #Load line」。**但它只钉位置与条数，没钉诊断号** ⇒ 诊断号 `BC2001` 落在测试的覆盖面之外。
- ⇒ **缺陷面因此缩小为两点**，而不是"机制没实现"：
  1. **诊断号错位**：一个刚刚被成功打开过的文件，因成环被报成 `BC2001 找不到文件`。既有测试恰恰证明"这里要有诊断、且只一条、报在 `#Load` 行"——所以该改的只是**码/文案**（例如点名 cycle），不是行为。本仓对"消息指向真原因"的要求（issue 15/28 同族）适用。
  2. **重复加载语义未规定，且与 C# 分叉**（新读数，见 §六）。

## 六、新增实测：判据是"祖先栈"而非"已加载过"；菱形两侧不同

| 形状 | 本 fork（Debug `vbi`，已运行） | C#（`csi 5.10.0-1.26380.3`，已运行） |
|---|---|---|
| 同一主文件里连写两次 `#Load "dup-part.vbx"`（**非环**，纯重复） | **执行两遍、零诊断**：`DUP-part-ran` ×2 ＋ `DUP-main`，exit 0 | 去重⇒**只执行一遍** |
| 菱形：`main` 载 `da`、`db`，两者都载 `shared.vbx`（shared 只打印，无声明） | `SHARED-c-ran / A-ran / SHARED-c-ran / B-ran / DIAMOND-main`（shared 跑**两遍**），exit 0 | `SHARED-c-ran / A-ran / B-ran / DIAMOND-main`（跑**一遍**），exit 0 |

⇒ 保护只在"文件是本加载链的祖先"时触发 ⇒ §一 的成环读数与这里的重复读数是**两套语义**，本条原先把它们混写成"已加载过"。
⇒ 菱形/重复这一格两侧行为不同；`VisualBasicScriptCompiler.vb:185-186` 的注释给了设计理由——"Loaded trees come first so their top-level code executes before the main file, **matching the original text-inline behavior**"。**但该理由已被下面的读数打穿**（见 §六·补）：两遍展开不是"语义差异"，而是**把合法脚本判成错误**。

## 六·补、决定性读数：共享库被两个文件各自 `#Load` 时，VB 误报重复声明（严重度↑）

形状：`dz.vbx` 依次 `#Load "za.vbx"`、`#Load "zb.vbx"`；`za` 与 `zb` **各自** `#Load "sharedz.vbx"`；`sharedz.vbx` 里有顶层 `Dim z As Integer = 9` ＋ 打印 `z`；`dz` 末尾打印 `z`。

| 侧 | 读数（已运行） |
|---|---|
| **本 fork**（Debug `vbi`） | `sharedz.vbx(2) : error BC30260: “z”已在此 type 中声明为“Private z As Integer”。` ＋ `sharedz.vbx(3) : error BC31429: “z”不明确，因为 type“Submission#0”中存在多种具有此名称的成员`（exit 1） |
| **C#**（`csi 5.10.0-1.26380.3`，同形状 `.csx`） | `SHAREDZ-ran z=9 / A-ran / B-ran / DIAMONDZ-main z=9`，**exit 0**（共享文件只算一次） |
| C# 纯重复（同一文件连 `#load` 两次） | 已运行：`DUP-part-ran / DUP-main` ⇒ **去重，只执行一遍**（本行原写"去重"时**未实测**，现已补跑证实） |

⇒ 影响面：**两个 `.vbx` 各自引入同一个公共库文件（最常见的复用形状）在本 fork 里根本编译不过**，而且诊断落在库文件自己身上（用户会以为是库写错了）。这不是措辞问题、也不是"设计自洽"，属"合法输入被拒"⇒ 按 `decisions.md` 的第一条判定原则（合法输入崩编译器/被误判即必修）本条从"中"升为**高**。
⇒ 与 §五.1 是**两个独立缺陷**：§五.1 是"成环时报了个误导的诊断号"；本格是"无环的共享加载被展开两遍并因此误判"。

## 七、状态与下一步（据 §六·补 重排优先级）

1. **【最高】修重复加载的去重语义**：让 `CollectLoadTrees` 对**本棵树内已展开过**的文件去重（对齐 C#，也消除 §六·补 的误判）。硬约束＝不得破坏 `TestNestedLoadDirective`（链式）与 `ScriptTests.vb:526 TestLoadDirectiveDoesNotShiftDiagnosticSpan`（跨树诊断位置不漂移，issue 01 的成果）；须新增"菱形 ＋ 共享库含顶层声明"的正格（断零诊断且取到值）与"成环仍只一条诊断"的反例锁。
2. **诊断号错位**（§五.1）：成环的诊断换成点名成因的码，并给 `TestLoadDirectiveCycleReportsAtLoadLine` **补上诊断号断言**（它现在只钉位置与条数，任意码都能过）。
3. **规格缺口**：`spec` 需写明"同一文件在一条加载链中被引用多次时只展开一次"（改完 #1 之后这就是新契约），中英两份同步；原先"文本内联语义"的说法不再成立，不得写进正本。

合法容器对照这条路不适用（`#Load` 只在脚本里合法，普通编译报 `BC36967`）。

**严重度**：高（§六·补＝合法脚本被拒；§一/§五.1＝诊断误导）。
