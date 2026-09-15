# 脚本顶层 `Dim x = <expr>` 不推断类型（D5 分歧，交用户裁决）

* 状态：**Open**
* 发现日期：2026-09-15
* 发现场景：初报「脚本顶层 LINQ `Group By` 抛 `InvalidCastException`」被判为**非缺陷**后的收尾（`../tasks/script-mode-coverage-parity/design-overview.md` §3、`design-detailed.md` §U1）。判定过程推翻了「容器特有缺陷」，剩下的唯一分歧就是本条。
* **性质**：**语义 / 能力分歧**，不是崩溃、不是误诊。**产品源码零改动**，本 issue 只登记与取证。

## 触发面

脚本顶层的 `Dim x = <expr>`（无 `As` 子句）**恒为 `Object` 字段**，`Option Infer On` 对它无效。C# 脚本的同一位置写 `var x = <expr>` **会推断**。

**后果链**（三条都在，逐条有实测）：

1. 顶层 `Dim q = <LINQ 查询>` 的 `q` 是 `Object` 字段；
2. 对 `Object` 调 `.Select(...)` 走**晚期绑定**；
3. 晚期绑定器对该匿名类型形状找不到 `Select` 重载 ⇒ 抛 `InvalidCastException`（`0x80004002`）。

第 1 步是本 issue 的对象；第 2、3 步是既有已测行为 + 普通晚期绑定语义，不属本条。

## 实测读数

| # | 形状 | 读数 | 来源 |
|---|---|---|---|
| 1 | 脚本顶层 `Dim q = <Group By 查询>`，**无 `As`** | 静态类型 = `Object`（重载决议判别） | 用例 `Scripting\VisualBasicTest\ScriptModeTopLevelInferenceTests.vb:90`（`TopLevelQueryField_IsObject`）；既有用例 `Scripting\VisualBasicTest\ScriptModeStatementConformanceTests.vb:606`（`TopLevelInferredField_IsObject_Conforms`）用**同一判据、不同重载对**（`Tell(Object)`/`Tell(Integer)`）测同一行为 |
| 2 | 同一查询放进**顶层 `Sub` 的局部** | 正常求值，`2:2,1:1,3:1` | 用例 `ScriptModeTopLevelInferenceTests.vb:110`（`SubLocalQuery_IsSequence`，同一重载对答 `sequence`）+ `:130`（`SubLocalQuery_Evaluates`） |
| 3 | **同一查询文本**加 `As` 子句（顶层字段，`As System.Collections.IEnumerable`） | 正常求值，`2:2,1:1,3:1` | 用例 `ScriptModeTopLevelInferenceTests.vb:153`（`TopLevelQueryWithAsClause_Evaluates`） |
| 4 | 顶层 `Dim values = {1,2,3,4}` 后 `From v In values` | **`BC36593`**：`Expression of type 'Object' is not queryable.` —— **编译器自己**把 `values` 报成 `Object` | 探针 `tmp\probes\u14\r5\field_infer.py` 的 `script-top-infer-linq`（源文件 `tmp\probes\u14\r5\script-top-infer-linq.vbx:2`）；文案条目 `Compilers\VisualBasic\Portable\VBResources.resx:3241` |
| 5 | 顶层 `Dim q = <查询>` 后对 `q` 调晚期绑定成员 | **`InvalidCastException`**，消息含成员名 `Select`（`Overload resolution failed because no Public 'Select' can be called with these arguments: …`） | 用例 `ScriptModeTopLevelInferenceTests.vb:172`（`TopLevelQueryField_LateBoundSelect_Fails`）；探针读数 exit `2147500034` = `0x80004002` = `COR_E_INVALIDCAST`（`tmp\probes\u14\r5\decisive.py` 的 `script-top-optioninfer-on`） |

**判别力说明（为什么不拿 `GetType()` 当判据）**：晚期绑定调 `Object.GetType()` 返回的是**运行时类型**，`Object` 字段与已推断字段两种情形**同值** ⇒ 该形状对「是否推断」不敏感（探针 `script-top-field` 得 `F=Int32` 即此坑，`design-detailed.md` §1.2 第 4 条）。用例一律用 `Tell(o As Object)` / `Tell(items As System.Collections.IEnumerable)` 的**重载决议**判别；`SubLocalQuery_IsSequence` 是证明该重载对确实有鉴别力的对照（不作此对照，`Object` 的读数可能是重载永不选 `IEnumerable` 造成的假象）。

## 根因

**VB 语言本身没有「字段类型推断」这一形式**，与脚本容器无关：

1. 脚本顶层的 `Dim` 是**字段**（提交类的字段），走的是字段声明的类型判定路径。
2. VB 字段声明的类型判定**没有脚本分支**：`Compilers\VisualBasic\Portable\Symbols\Source\SourceMemberFieldSymbol.vb:186-205` 在无 `As` 子句时一律经 `DecodeModifiedIdentifierType(..., ModifiedIdentifierTypeDecoderContext.FieldType)`，`Option Strict On` 下配 `ERR_StrictDisallowImplicitObject`（`BC30209`，`:193`）——即「无 `As` 子句的字段 = `Object`」。
3. VB 里唯一带「推断字段类型」标记的字段是 **`Const`**（`SourceMemberFieldSymbol.vb:558-568` 置 `SourceMemberFlags.InferredFieldType`，该标记的唯一消费者是 `SourceFieldSymbol.vb:145-150` 的 `HasDeclaredType`），**普通 `Dim` 字段根本不进这条路**。注意它与 `Option Infer` 的关系是反的：标记由**语法形状**决定（`Const` 且无类型字符 / 无 `As` / 无 `?` / 无数组界），`:186` 的 `Not (isConst AndAlso binder.OptionInfer)` 只用来在 `Option Infer On` 时**抑制 `BC30209` 的报出**，不是开关推断。
4. 该路径**对容器不敏感**（检索证据：`SourceMemberFieldSymbol.vb` 全文件 `IsScriptClass` / `TypeKind.Submission` / `IsSubmission` **零命中**，`grep -c` 读数为 `0`）⇒ 普通 `Class` 的同形状字段同样是 `Object`。脚本侧的旁证：顶层 `Dim` 在 `Option Strict On` 下同样报 `BC30209`（`ScriptModeStatementConformanceTests.vb:623`）。

**关于 `Const` 那条例外的补充实证**（`Compilers\VisualBasicSymbolTest\SymbolsTests\Source\FieldTests.vb`）：`Bug9902_ValuesForConstField`（方法 `:385`）在同一源码上跑 `Option Strict` × `Option Infer` 四组合（期望块 `:388-405`：`BC30209` 在 `:391`，指源码 `:392`；实际脚本文件块 `:406-418`，`Private Const Field2 = 42` 在 `:412`；四组合由 `:407-408` 的双层 `For Each` 驱动，`index` 在**内层**递增 ⇒ index1 = `Strict On / Infer Off`）。四组合里**只有 `Strict On / Infer Off` 报 `BC30209`** ⇒ 这就是第 3 条里 `:186` 的 `Not (isConst AndAlso binder.OptionInfer)` 抑制的实证，也再次说明 `Option Infer` 在**字段**上只影响「报不报 `BC30209`」，不影响类型判定。

**C# 侧有对应形式**（分歧的由来）：C# 字段符号在**脚本类**里走的是另一条分支，`Compilers\CSharp\Portable\Symbols\Source\SourceMemberFieldSymbol.cs:530` 判 `if (!ContainingType.IsScriptClass)`，脚本类落 `else`（`:550-551`）调到 `BindTypeOrVarKeyword(..., out isVar)` 并接受 `isVar` ⇒ **C# 脚本顶层 `var x = <expr>` 会推断**。CS0825 的文案本身也把这个例外写明（`CSharpResources.resx:2302`：`… may only appear within a local variable declaration or in script code`）。

## 预期行为（两种，交用户裁决）

**这一条不自行改语义**——无论选哪种，都属脚本语义变更，须用户定案。

* **备选 A（对齐全 csi，按 D5）**：让脚本顶层字段参与推断（等价于给 VB 脚本一个「字段版 `var`」）。判据：`Dim q = <查询>` 后的 `q` 静态类型为该查询的自然类型，且 `Option Infer On/Off` 有可判的差异。VB 有 C# 无的概念（晚期绑定、`Dim` 亦是字段/局部两种形态），须说明为何分叉不能照搬。
* **备选 B（保持现状，改为显式提示）**：承认 VB 无字段推断，但在脚本顶层「无 `As` 子句的 `Dim`」上给出比今天更强的用户可见信号。今天只在 `Option Strict On` 下报 `BC30209`（`ScriptModeStatementConformanceTests.vb:623`），默认（`Option Strict Off`）下**零提示**、静默变 `Object`，用户要到运行期才撞上 `InvalidCastException`。

**D5 依据**：`../decisions.md` 的 **D5** 节（基础功能的落地细节以 C# / csi 实现为设计蓝本）——「C# 脚本的 `var` 字段推断」正落在 D5 的适用面内；其**边界**段的其它理由（实现可行性、与 D5 的同形性、机制收益与代价、规范的可表达性）仍须逐条论证。

## 修复方向

**未定**——取决于用户在 A / B 之间的裁决。

* 若取 A：VB 侧需在 `SourceMemberFieldSymbol.vb` 的字段类型判定路径上为脚本类开出推断分支（对偶于 C# 的 `SourceMemberFieldSymbol.cs:530` / `:550-551`），并处理 `Option Infer Off` / `Option Strict On` 的交互（今天 `BC30209` 的报点 `:192-193` 要与新分支共存）。
* 若取 B：在无 `As` 子句的顶层 `Dim` 上补一条与 `Option Strict` 无关的提示（新诊断码或警告），须评估与既有 `BC30209` 的重复报出关系。

**两种备选都不得在本任务的补测单元里顺手实现**（`design-detailed.md` §4 / §U1 的共同纪律）。

## 已有护栏（本 issue 的用例落点）

`Scripting\VisualBasicTest\ScriptModeTopLevelInferenceTests.vb` 五格，全绿：

| 格 | 方法 | 断言 |
|---|---|---|
| ① 无 `As` 的顶层查询字段 | `TopLevelQueryField_IsObject`（`:90`） | 重载决议得 `object` |
| 对照（重载对的鉴别力） | `SubLocalQuery_IsSequence`（`:110`） | 同一查询在 `Sub` 局部得 `sequence` |
| ② 同一查询在 `Sub` 局部 | `SubLocalQuery_Evaluates`（`:130`） | `2:2,1:1,3:1` |
| ③ 同一查询文本 + `As` 子句 | `TopLevelQueryWithAsClause_Evaluates`（`:153`） | `2:2,1:1,3:1` |
| ④ 负向：对 `Object` 字段调晚期绑定成员 | `TopLevelQueryField_LateBoundSelect_Fails`（`:172`） | `InvalidCastException` + 消息含 `Select` |

① 与 ② 给出**不同结果**（`object` vs `sequence`），即该用例对「是否推断」敏感（`design-detailed.md` §1.4 的判别性论证）。

## 相关

* 判定全文（本条是本轮 A 组三条里唯一剩下的分歧）：`../tasks/script-mode-coverage-parity/design-overview.md` §3、`../tasks/script-mode-coverage-parity/design-detailed.md` §U1。
* 顶层推断字段的既有行为已有用例：`Scripting\VisualBasicTest\ScriptModeStatementConformanceTests.vb:606` / `:623`（本 issue 与之**不重复**：那里断言的是行为本身，本 issue 记录的是与 csi 的**分歧**及裁决请求）。
* 同族「顶层声明与普通容器不同」的已收口缺陷（与本条不同源，本条非崩溃）：`issue-submission-shared-field-initializer-typeload.md`（05）、`issue-submission-implicit-type-member-asserts.md`（07）。
