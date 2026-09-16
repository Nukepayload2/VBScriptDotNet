# 构造泛型 / 嵌套泛型类型作 `globalsType` 时宿主对象不绑定（嵌套泛型还会崩断言）

* 状态：**Fixed**（已验证，commit 待作者提交后补）
* 发现日期：2026-09-15
* 修复日期：2026-09-16
* 发现场景：U3「宿主对象（`globalsType`）绑定语义」补测单元对标 C# `HostObjectBinding_PublicGenericClassMembers`（`{{Roslyn}}\src\Scripting\CSharpTest\InteractiveSessionTests.cs:1545`）时，VB 对偶格**无法照抄 C# 期望值**，遂按「补测不顺手改产品码」的纪律取证上报（`../tasks/script-mode-coverage-parity/design-detailed.md` §U3 pass 条件第 5 条）。
* **性质**：**实现缺陷**（VB 侧缺 C# 已有的解析手段），**且含进程级失败**。产品源码零改动，本 issue 只登记与取证。

## 触发面

**症状 A · 构造泛型作 `globalsType` ⇒ 宿主对象完全不绑定（`BC30451`）**

```vb
' 宿主类型（本仓 Scripting\VisualBasicTest 内的普通公共类型）
Public Class GenericMembers(Of T)
    Public Function G() As T
        Return Nothing
    End Function
End Class

VisualBasicScript.Create("? G()", options, globalsType:=GetType(GenericMembers(Of String)))
```

宿主对象的成员**一个都不进作用域**：脚本对 `G()` 报 `BC30451`，而不是取到宿主对象的成员。同类形状在 C# 通过（`InteractiveSessionTests.cs:1545` 断言 `G()` 求值为 `null`）。

**症状 B · 嵌套泛型作 `globalsType` ⇒ `Debug.Assert` 被触发（进程级失败）**

```vb
Public Class Outer
    Public Class GenericMembers(Of T)          ' 嵌套泛型
        Public Function G() As T
            Return Nothing
        End Function
    End Class
End Class

VisualBasicScript.Create("? G()", options, globalsType:=GetType(Outer.GenericMembers(Of String)))
```

宿主类型解析路径上的 `Debug.Assert` 被触发（状态见下）。

**两个症状的分界**：两者都由「反射 `FullName` 被当元数据名用」引起，但**出口不同**——顶层构造泛型只走 `GetTopLevelTypeByMetadataName`，查不到 ⇒ 返回 `Nothing` ⇒ 静默无宿主对象；嵌套构造泛型先命中 `+` 嵌套分支，走进按片段查嵌套类型的循环 ⇒ 断言。所以顶层症状**没有诊断**、也没有崩溃，只有 `BC30451`，这正是它容易被误读成「脚本里就该写不出泛型」的原因。

## 实测读数

| # | 形状 | 读数 | 来源 |
|---|---|---|---|
| 1 | `globalsType = GetType(<顶层泛型类>(Of String))`，脚本 `? G()` | **`BC30451`**（宿主对象未绑定） | 用例 `Scripting\VisualBasicTest\ScriptModeHostObjectConformanceTests.vb:217`（`HostObjectBinding_PublicGenericClassMembers`，锚点为 `<Fact>` 方法声明行） |
| 2 | 同上，**该泛型类的 `FullName`** | 含 `[[`（反射形，带类型实参全名） | 同一用例内**运行期**断言 `Assert.Contains("[[", globalsType.FullName)`；实读值形如 ``…HostObjectGenericMembers`1[[System.String, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]]`` |
| 3 | `globalsType = GetType(<嵌套泛型类>(Of String))`，脚本 `? G()` | **`Xunit.Sdk.TraceAssertException`**，消息 `!typeName.Contains(MetadataHelpers.DotDelimiterString) \|\| typeName.IndexOf(MetadataHelpers.MangledNameRegionStartChar) >= 0`（**实锤**） | 补测单元实施期的首版用例（夹具当时嵌在 `HostObjectFixtures` 内）实跑；栈见「崩溃链」节 |
| 4 | 同上形状在 **Release** 下 | **无断言、编译存活、落回症状 A 的 `BC30451`**（**实锤**，验证者独立探针 `RELEASE` 读数 `COMPILE SURVIVED` + `BC30451`） | 验证者独立探针 `tmp\probes\u3-verify\`（git-ignored；`ProjectReference` 指向 Debug/Release 版 Scripting 项目，**不改产品源码**） |
| 5 | 「同类泛型类，改用 `Inherits` 闭合」作对照 | **正常绑定**：`? G() Is Nothing` → `True` | 用例 `ScriptModeHostObjectConformanceTests.vb:238`（`HostObjectBinding_ClosedGenericBaseMembers`）——它的元数据名是普通名，证明缺陷只在「`globalsType` 用构造泛型」这一入口，不在「泛型成员」本身 |

**症状 B 的断言状态**：断言**被触发**是**实锤**（第 3 行，有完整栈）。「无监听器的 Debug 宿主下是进程终止」同样是**实锤**——验证者用独立探针在普通控制台宿主（`dotnet run`，无 xunit 的 `Trace` 监听器）下实测：**退出码 35**，stderr 先打 `Process terminated.` ⇒ **进程级失败**，不是「测试失败」。两处升级均由验证者独立探针实证（`tmp\probes\u3-verify\`，git-ignored，**不改产品源码**）。

**判别性论证**（本 issue 的核心主张：「宿主对象没绑定」而不是「泛型成员不可访问」）：第 5 行是**同一泛型类**的闭合版本，`? G()` 正常绑定并返回 —— 若缺陷在「泛型成员绑定」，该对照同样会失败。第 2 行把 `FullName` 的形状**在运行期断言**而不是靠注释声称，使「反射形被当元数据名」这一前提可机械复核。

## 根因

**VB 侧把反射 `FullName` 当元数据名用**：

`Compilers\VisualBasic\Portable\Compilation\VisualBasicCompilation.vb:933-945`：

```vb
Friend Function GetHostObjectTypeSymbol() As TypeSymbol
    Dim hostObjectType = Me.HostObjectType
    If hostObjectType Is Nothing OrElse hostObjectType.FullName Is Nothing Then
        Return Nothing
    End If

    Dim result As TypeSymbol = GetTypeByMetadataName(hostObjectType.FullName)      ' :939
    If result Is Nothing AndAlso hostObjectType.FullName.Contains("+"c) Then       ' :940
        result = GetTypeByMetadataName(hostObjectType.FullName.Replace("+"c, "."c)) ' :941
    End If

    Return result
End Function
```

`:941-943` 的 `+`→`.` 回退是**嵌套类型**的兜底（`+` 是反射的嵌套分隔符，`.` 是 VB 源级写法），**不处理泛型实参**：构造泛型的 `FullName` 把类型实参连同程序集全名一并展开（读数第 2 行），既不是元数据名，也不是 `+`→`.` 能修的形。

**C# 侧是蓝本**（`Compilers\CSharp\Portable\Compilation\CSharpCompilation.cs:1866-1885`）：

```csharp
protected override ITypeSymbol? CommonScriptGlobalsType
    => GetHostObjectTypeSymbol()?.GetPublicSymbol();

internal TypeSymbol? GetHostObjectTypeSymbol()
{
    if (HostObjectType != null && _lazyHostObjectTypeSymbol is null)
    {
        TypeSymbol? symbol = Assembly.GetTypeByReflectionType(HostObjectType);   // :1873 —— 基于反射类型查
        if (symbol is null)
        {
            … MissingMetadataTypeSymbol.TopLevel …                              // :1881-1885 兜底
        }
```

C# 走 `Assembly.GetTypeByReflectionType(Type)`，**不经字符串**，所以构造泛型（乃至数组、byref 等反射形）都能对上；查不到时还有 `MissingMetadataTypeSymbol` 兜底而不是 `Nothing`。VB 侧两者都缺。

**注意 `:935` 的 `FullName Is Nothing` 前置检查**：它只挡住了「无法命名」的类型，不分辨「能命名」与「是元数据名」——构造泛型的 `FullName` 非空，因此顺利走到 `:939`。

## 崩溃链（症状 B 的三个锚点，实锤）

栈（实跑读出，自下而上读）：

1. `Binder_Lookup.vb:920` `Dim hostObjectType = binder.Compilation.GetHostObjectTypeSymbol()`（成员查找失败后落到宿主对象）
2. `VisualBasicCompilation.vb:939` `GetTypeByMetadataName(hostObjectType.FullName)` —— **栈上就是 `:939`**，即崩溃发生在**第一次**查询内，`:940-942` 的回退根本没机会跑（**实锤**：栈行号）
3. `Symbols\AssemblySymbol.vb:580` `If metadataName.Contains("+"c) Then` —— 名字里有 `+`，走嵌套分支
4. `AssemblySymbol.vb:582` `metadataName.Split(s_nestedTypeNameSeparators)` → `AssemblySymbol.vb:596` `MetadataTypeName.FromTypeName(parts(i))`（`:595` 是 `While i < parts.Length` 循环头，调用在 `:596`；验证者实跑栈逐字为 `AssemblySymbol.vb:line 596`）
5. `Compilers\Core\Portable\MetadataReader\MetadataTypeName.cs:154` `Debug.Assert(!typeName.Contains(MetadataHelpers.DotDelimiterString) OrElse typeName.IndexOf(MetadataHelpers.MangledNameRegionStartChar) >= 0)`

对 `parts(1)` = ``GenericMembers`1[[System.String, System.Private.CoreLib, Version=10.0.0.0, …]]``：含 `.`（来自**类型实参的程序集全名**）却**不含** `<`（`MangledNameRegionStartChar` 的字面值，`MetadataHelpers.cs:57`：`MangledNameRegionStartChar = '<'`——元数据约定里 mangled 区以 `<` 开头，如 `<PrivateModule>`；反射形里不会出现）⇒ 断言必炸（**实锤**，栈已确证）。

**判定**：按作者判定原则「**崩编译器是 bug**。要么让它别崩、正常跑；要么报诊断说『脚本不支持这样用』」（`../tasks/script-mode-coverage-parity/README.md:10` 与 `:167`，另见 `../tasks/script-top-level-crashes/README.md:7`、`../tasks/script-top-level-crashes-2/README.md:7`），症状 B **不可辩护为有意分歧**——它既没正常跑，也没报诊断，而是把断言留给编译器自己。

## 预期行为

**不需要用户裁决**（与 issue 21 的 D5 分歧不同）：

1. 症状 B 是崩溃，按上引作者原则不可辩护；
2. 症状 A 有 C# 同格对照**在同一形状上通过**（`InteractiveSessionTests.cs:1545`），属 D5（`../decisions.md` D5：基础功能的落地细节以 C# / csi 为设计蓝本）面内的实现缺口，不是语义分歧。

预期：`globalsType` 为构造泛型（含嵌套泛型）时，宿主对象**照常绑定**，可访问的类型参数按闭合后的实参判定——即与 `InteractiveSessionTests.cs:1545` 同形同值。

## 修复方向

**镜像 C# 的 `GetTypeByReflectionType`**（`CSharpCompilation.cs:1873`），并补 `MissingMetadataTypeSymbol` 式兜底（`:1881-1885`）。

* **保留** `VisualBasicCompilation.vb:940-942` 的 `+`→`.` 回退——那是 VB 侧有意的兜底，新路径不是替换它，而是先按反射类型解析、失败再落回字符串路径。
* 注意 `:935` 的 `FullName Is Nothing` 前置检查在新路径下的角色（反射类型可直接判定，不必依赖 `FullName`）。
* **不得在本补测单元里顺手实现**（`design-detailed.md` §4 / §U3 pass 条件第 5 条：补测单元零产品改动）。

## 修复后的新增行为变化（须补的回归用例）

今天**静默**、修复后会改判的形状，必须一并纳入回归：

1. `Scripting\VisualBasicTest\ScriptModeHostObjectConformanceTests.vb:217` 的用例断言是**金丝雀**，注释已明写 `BC30451` 是「宿主对象缺失」的证据而非规范；**修复落地后本用例必须改写为 C# 期望**：`? G() Is Nothing` → `True`（与同文件 `:238` 的 `HostObjectBinding_ClosedGenericBaseMembers` 同断言形态）。**它失败即提醒改写**，不得读成「修复改坏了」。
2. 症状 B 的形状（嵌套泛型 `globalsType`）今天**零诊断 + 断言**；修复后应正常绑定，须新增一条覆盖嵌套泛型的用例（本单元为避开进程级失败而**未**纳入，见下「已有护栏」的说明）。
3. 数组 / `ByRef` 等其它「`FullName` 非普通元数据名」的 `globalsType` 形是否有同类症状：**未查**（补测单元未穷举入口），修复单元须一并扫。

**上述三项的落地状态（2026-09-16 修复单元）**：第 1 项**已改写**（并追加实参落点断言）；第 2 项**已新增**；第 3 项**已扫**（数组有同类症状、已一并修复与覆盖；`ByRef` / 开放泛型 / 值类型 / 指针由 `IsValidHostObjectType` 提前拒绝、行为未改）。逐形读数见下「修复」节的扫描表。

## 已有护栏（本 issue 的用例落点）

`Scripting\VisualBasicTest\ScriptModeHostObjectConformanceTests.vb`（U3 单元新增，13 格全绿）：

| 用途 | 方法 | 落点 |
|---|---|---|
| **金丝雀**（症状 A） | `HostObjectBinding_PublicGenericClassMembers` | `:217` |
| 正向配对（证明缺陷在 `globalsType` 入口而非泛型成员） | `HostObjectBinding_ClosedGenericBaseMembers` | `:238` |

**未纳入护栏**：症状 B（嵌套泛型）在修复前**故意不写用例**——它会终止进程/xunit 运行，把「补测单元必须全绿」的验收打成不可判定。这正是本 issue 需要修复单元介入的直接理由。（修复后该缺口已由 `HostObjectBinding_NestedGenericClassMembers` 闭合，见下。）

## 修复（2026-09-16）

### 主路径：镜像 C# 的反射类型解析

`Compilers\VisualBasic\Portable\Compilation\VisualBasicCompilation.vb` 的 `GetHostObjectTypeSymbol`（`:933-957`）由「反射 `FullName` 当元数据名用」改为**先按反射类型解析、失败再落回字符串路径**：

```vb
If hostObjectType Is Nothing Then Return Nothing                          ' :935
Dim result As TypeSymbol = GetTypeByReflectionType(hostObjectType)        ' :943（镜像 CSharpCompilation.cs:1873）
If result Is Nothing AndAlso hostObjectType.FullName IsNot Nothing Then
    result = GetTypeByMetadataName(hostObjectType.FullName)                ' :948 保留的字符串回退
    If result Is Nothing AndAlso hostObjectType.FullName.Contains("+"c) Then
        result = GetTypeByMetadataName(hostObjectType.FullName.Replace("+"c, "."c))   ' :950
    End If
End If
```

* `VisualBasicCompilation.GetTypeByReflectionType`（`:2055`）是**既有**方法（转调 `Assembly.GetTypeByReflectionType`），本 issue 之前只有 `SynthesizedInteractiveInitializerMethod.vb:169` 一个调用方。
* `:940-942` 的 `+`→`.` 回退**保留**（VB 侧有意的嵌套类型兜底），角色由「主路径」降为「回退」。
* `:935` 的 `FullName Is Nothing` 前置检查由「整个函数的门」收窄为**字符串回退的门**（反射路径不需要名字）。
* **未加 `MissingMetadataTypeSymbol` 兜底**（与 C# 的唯一有意差异，理由见下节「兜底评估」）。

### 第二处缺陷：反射行走无法处理「泛型外层里的嵌套类型」

镜像 C# 后，**嵌套类型且外层泛型**的形状（`GetType(Outer(Of String).Inner)`）并不随之修好：`Type.DeclaringType` 对嵌套类型返回**外层类型的泛型定义**（**实锤**：`GetType(GenericOuter(Of String).NestedPlain).DeclaringType` 实测为 `GenericOuter`1[T]`，`ContainsGenericParameters=True`），而 `AssemblySymbol.GetTypeByReflectionType` 的 `Debug.Assert(Not type.ContainsGenericParameters)`（`Symbols\AssemblySymbol.vb:723`）随即被触发 ⇒ 换了条断言的**同样崩溃**。

C# 侧**同形同崩**（**实锤**，独立 C# 探针：`CSharp\Portable\Symbols\AssemblySymbol.cs:782` 的断言，递归点在 `:830`）⇒ 这是**两语言共享的上游限制**，不是 VB 特有缺口。按作者原则「崩编译器是 bug」仍须修，故在 VB 侧修 `Symbols\AssemblySymbol.vb:GetTypeByReflectionType`（C# 侧不动，偏差登记见 `../upstream-merge.md` §2.22）：

* `:765-772` —— 递归解析最外层类型前，先用「本类型为外层携带的实参」把外层泛型定义**闭合**（`MakeGenericType`）；
* `:774` —— 实参游标由 0 改为 `rootArity`（外层实参已在闭合时消耗）；
* `:833-837` —— `ApplyGenericArguments` 增 `length = 0` 早退（非泛型嵌套类型自己没有类型参数；原先会走到 `symbol.Construct(空)`，对已替换类型抛 `InvalidOperationException` —— **实锤**，修复中途的用例实跑栈顶即 `SubstitutedNamedType.SpecializedNonGenericType.Construct`）。

### 兜底评估：VB 侧不加 `MissingMetadataTypeSymbol`（证据）

C# 在查不到时给 `MissingMetadataTypeSymbol`（`CSharpCompilation.cs:1881-1885`），其**唯一可见作用**是让 `HostObjectModelBinder` 把错误类型转成 `CS0103`（消息含缺失程序集名，`Compilers\CSharp\Portable\Binder\HostObjectModeBinder.cs:36-44`）。**VB 没有对应 binder，且四个调用方全都过滤错误类型**：

| 调用方 | 判据 |
|---|---|
| `Binding\Binder_Lookup.vb:920-921` | `If hostObjectType IsNot Nothing AndAlso hostObjectType.Kind <> SymbolKind.ErrorType Then` |
| `Binding\Binder_Lookup.vb:2045-2046` | 同款过滤 |
| `Lowering\SynthesizedSubmissionFields.vb:55-56` | 同款过滤 |
| `Binding\Binder_Expressions.vb:2620-2629` | 与 `memberDeclaringType` 逐个 `TypeSymbol.Equals` 比对（错误类型永不命中），实际等价于过滤 |

`CommonScriptGlobalsType`（`:927-931`）是唯一的直通调用方，但 `Compilation.ScriptGlobalsType`（`Compilation.cs:1046`）的**唯一消费者**是 C# 的 `SymbolDisplayVisitor.cs:454`，VB 侧无消费者。⇒ 在 VB 侧返回错误类型与返回 `Nothing` **在全部调用点行为完全一致**，加兜底等于引入死代码（还要新增 `AssemblyIdentity.FromAssemblyDefinition` 依赖与一条错误文案）。**故不加**；若日后需要「宿主类型不可解析时给诊断」，落点应在 binder，不在 `GetHostObjectTypeSymbol`。

### 修复后的实测读数

独立探针（`tmp\probes\u13-impl\`，git-ignored，`ProjectReference` 指向本仓 Scripting 项目，**不改产品源码**），每形**单进程**运行（断言会终止宿主，故必须一形一进程）：

| # | 形状 | 修复前 | 修复后 |
|---|---|---|---|
| 1 | 对照 `GetType(PlainMembers)`，`? X` | 0 诊断，`RUN OK ReturnValue=1` | 同左 |
| 2 | **症状 A** 顶层构造泛型，`? G()` | `BC30451`，无宿主对象 | **0 诊断，`RUN OK ReturnValue=(null)`** |
| 3 | **症状 B** 嵌套泛型，`? G()` | **断言，`EXIT=35`** | **0 诊断，`RUN OK ReturnValue=(null)`** |
| 4 | 泛型外层里的非泛型嵌套类型，`? G()` | **断言，`EXIT=35`** | **0 诊断，`RUN OK ReturnValue=1`** |
| 5 | 泛型外层里的泛型嵌套类型，`? G()` | **断言，`EXIT=35`** | **0 诊断，`RUN OK ReturnValue=0`** |
| 6 | 数组 `GetType(Integer())`，`? Length` | `BC30451` | **0 诊断，`RUN OK ReturnValue=0`** |
| 7 | 三维数组 `GetType(Integer(,,))`，`? Length` | `BC30451` | **0 诊断，`RUN OK ReturnValue=0`** |
| 8 | 构造泛型的数组，`? Length` | `BC30451` | **0 诊断，`RUN OK ReturnValue=0`** |
| 9 | `ByRef`（`MakeByRefType`） | `COMPILE THREW ArgumentException` | 同左（未改） |
| 10 | 开放泛型（`GetType(OpenGenericMembers(Of ))`） | `COMPILE THREW ArgumentException` | 同左（未改） |
| 11 | 值类型 `GetType(Integer)` | `COMPILE THREW ArgumentException` | 同左（未改） |
| 12 | 指针（`MakePointerType`） | `COMPILE THREW ArgumentException` | 同左（未改） |
| 13 | 开放泛型外层的嵌套泛型 | `COMPILE THREW ArgumentException` | 同左（未改） |

第 9–13 行的异常是 `Compilation.IsValidHostObjectType`（`Compilers\Core\Portable\Compilation\Compilation.cs:559-563`）经 `ValidateScriptCompilationParameters`（`:250-255`）抛的 `ArgumentException`（消息 `ReturnTypeCannotBeValuePointerbyRefOrOpen`），**修复前后一致**；它同时是「反射行走里的 `Debug.Assert(Not type.IsByRef)` / `Debug.Assert(Not type.ContainsGenericParameters)` 不可达」的依据。

**实参落点读数**（把「有没有崩」升级为「实参有没有落在正确的层」，`? TypeArgumentName()` 一类调用）：顶层构造泛型 → `String`；嵌套泛型 → `String`；泛型外层里的非泛型嵌套类型 → `String`；泛型外层里的泛型嵌套类型 → `Int32|String`（自身实参 `Int32`、外层实参 `String`）。

### 修复后的用例落点

`Scripting\VisualBasicTest\ScriptModeHostObjectConformanceTests.vb`：

| 用途 | 方法 | 状态 |
|---|---|---|
| **金丝雀**（症状 A） | `HostObjectBinding_PublicGenericClassMembers` | **改写为 C# 期望**：`? G() Is Nothing` → `True` ＋ `? TypeArgumentName()` → `"String"` |
| 对照（`FullName` 是普通元数据名，走字符串回退） | `HostObjectBinding_ClosedGenericBaseMembers` | 不变 |
| 症状 B（嵌套泛型） | `HostObjectBinding_NestedGenericClassMembers` | **新增** |
| 泛型外层里的嵌套类型（非泛型 / 泛型 + 实参互换判别对） | `HostObjectBinding_NestedTypeInConstructedGenericOuter` / `HostObjectBinding_GenericNestedTypeInConstructedGenericOuter` | **新增** |
| 三层嵌套（C# `TypeResolutionTests.cs:142` 的形状） | `HostObjectBinding_DeeplyNestedGenericTypeArguments` | **新增** |
| 数组（一维 / 三维 / 构造泛型的数组） | `HostObjectBinding_ArrayGlobalsType` | **新增** |
| 被拒形状边界 | `HostObjectBinding_UnsupportedGlobalsTypeShapesAreRejected` | **新增** |

`Scripting\VisualBasicTest` 程序集全量：修复前 **480 passed / 0 failed**，修复后 **486 passed / 0 failed**（净增 6 条：改写 1 条 + 新增 6 条 − 改写仍计 1 条）。

## 相关

* C# 基准格：`{{Roslyn}}\src\Scripting\CSharpTest\InteractiveSessionTests.cs:1545`（`HostObjectBinding_PublicGenericClassMembers`）、夹具 `:1519-1525`（`M<T>`）。
* 生产锚点：`Compilers\VisualBasic\Portable\Compilation\VisualBasicCompilation.vb:933-957`（VB 侧，修复后）/ `Compilers\CSharp\Portable\Compilation\CSharpCompilation.cs:1866-1885`（C# 蓝本）。
* 崩溃链：`Symbols\AssemblySymbol.vb:580,582,596` + `Compilers\Core\Portable\MetadataReader\MetadataTypeName.cs:154`（`MangledNameRegionStartChar` 的字面值在 `Compilers\Core\Portable\MetadataReader\MetadataHelpers.cs:57`）。
* 同一解析器的调用方（修复时须一并复核）：`Binding\Binder_Lookup.vb:920`（成员查找）、`:2045`（补全用符号表）、`Lowering\SynthesizedSubmissionFields.vb:55`（`<host-object>` 字段类型）、`Binding\Binder_Expressions.vb:2620`（宿主对象接收者构造）。
* 任务上下文：`../tasks/script-mode-coverage-parity/design-detailed.md` §U3、`../tasks/script-mode-coverage-parity/test-plan.md` §U3。
