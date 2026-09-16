' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' ==================================================================================================================
' U8 · ObjectFormatter 代理族与异常栈渲染（design-detailed.md §U8）
'
' 本文件是 C# 基线 {{Roslyn}}\src\Scripting\CSharpTest\ObjectFormatterTests.cs 的 VB 移植。
' 期望值一律取自本文件实测运行结果（见每条用例的注释），不照抄 C# 字面量：VB 的类型名拼写、数组语法、泛型语法、
' 常量前缀都与 C# 不同（Integer() / (Of T) / Nothing / &H / [AStr] 而非 int[] / <T> / null / 0x / [AStr] 之外的形式）。
' 每一格都写明「凭什么能失败」——把该格断言的行为撤销后这条用例必须失败。
'
' ------------------------------------------------------------------------------------------------------------------
' C# 基线命名族格数（实测，design-detailed.md §U8 的「32 = 3 + 29」在本命令下不可复现，以本节读数为准）
'
' 命令逐字（工作目录 {{Roslyn}}\src\Scripting\CSharpTest）：
'   grep -oE "public void [A-Za-z0-9_]+" ObjectFormatterTests.cs | wc -l                        → 52
'   grep -nE "^\s*//.*public void" ObjectFormatterTests.cs                                      → 1 行（:733）
'   grep -nE "public void [A-Za-z0-9_]+" ObjectFormatterTests.cs | grep -vE "^\s*[0-9]+:\s*//" | wc -l → 51
'   grep -oE "public void DebuggerDisplay_[A-Za-z0-9_]+" ObjectFormatterTests.cs | sort -u       → 2 个
'   grep -oE "public void DebuggerProxy_[A-Za-z0-9_]+" ObjectFormatterTests.cs | grep -v ConcurrentBag | wc -l → 31
'
' 读数（实锤）：真实测试方法 51 个（`[Fact]`/`[ConditionalFact]` 标注，含 7 个 `[Fact(Skip=…)]`）。
'   命名族 = 33 个 = DebuggerDisplay_* 2 个（ParseSimpleMemberName :119 / Inherited :263）
'                          + DebuggerProxy_* 31 个；
'   若把不带下划线的 DebuggerDisplay（:161）也算作该族成员则为 34 个。
'   StackTrace_* = 7 个；其余无名/一次性用例 = 11 个。
'   §U8 写的「32 个命名族 = 3 + 29」四种计法（含/不含 :733 注释行 × 含/不含裸 DebuggerDisplay）分别给出 34 / 35 / 33 / 34，
'   都不是 32（推测：该数来自 design-detailed.md §B.1 的族表而非对文件的 grep）。本文件按 33 计。
'   :733 的 DebuggerProxy_FrameworkTypes_ConcurrentBag 被注释掉（`//[Fact]` 上方还有一条 TODO），不参与运行，不计入。
'
' ------------------------------------------------------------------------------------------------------------------
' 夹具 ↔ C# 测试方法对账表
'
' 左列 = Helpers\ObjectFormatterFixtures.vb（本单元的核心输入，未被本单元修改）里的每一个夹具；
' 右列 = 承载它的测试。凡由本文件新增的用例都写方法名，本文件不重复覆盖的写既有用例的 `文件:行号`。
'
' | 夹具（ObjectFormatterFixtures.vb）            | C# 侧对应                                        | VB 侧测试（本文件 = 本文件方法名；否则给 文件:行号）          |
' |-----------------------------------------------|--------------------------------------------------|----------------------------------------------------------------|
' | Outer / Outer.Nested(Of T)                    | Objects（:32）                                   | 既有 ObjectFormatterTests.vb:58（Objects）                     |
' | A(Of T) 及其 B/C/D/E、A(Of T).X               | Objects（:32）                                   | 既有 ObjectFormatterTests.vb:58（Objects）                     |
' | Sort（**VB 侧改名**：aB→aB2、ad→ad2，见下）  | Objects（:32）                                   | 既有 ObjectFormatterTests.vb:58（Objects）                     |
' | Signatures / Signatures.Arrays                | ArrayMethodParameters（:90）                     | 既有 ObjectFormatterTests.vb:101（ArrayMethodParameters）       |
' | RecursiveRootHidden                           | RecursiveRootHidden（:107）                      | RecursiveRootHidden                                            |
' | RecursiveProxy（Node/Proxy）                  | DebuggerProxy_Recursive（:287）                  | DebuggerProxy_Recursive                                        |
' | InvalidRecursiveProxy                         | DebuggerProxy_Recursive（:287，第二段）          | DebuggerProxy_Recursive（同一条，断言 StackOverflowWhileEvaluating）|
' | ComplexProxyBase                              | DebuggerDisplay（:161，`Goo`/`_26_5`）           | DebuggerDisplay                                                |
' | ComplexProxy（全部 30+ 形状）                 | DebuggerDisplay（:161，第一段）                  | DebuggerDisplay                                                |
' | TypeWithComplexProxy                          | DebuggerDisplay（:161，第二段）                  | DebuggerDisplay                                                |
' | TypeWithDebuggerDisplayAndProxy（Proxy）      | DebuggerProxy_DebuggerDisplayAndProxy（:272）    | DebuggerProxy_DebuggerDisplayAndProxy                          |
' | C                                             | DebuggerProxy_FrameworkTypes_Array（:378）       | DebuggerProxy_FrameworkTypes_Array                             |
' | BaseClassWithDebuggerDisplay                  | DebuggerDisplay_Inherited（:263）                | DebuggerDisplay_Inherited                                      |
' | InheritedDebuggerDisplay                      | DebuggerDisplay_Inherited（:263）                | DebuggerDisplay_Inherited                                      |
' | ToStringException                             | DebuggerDisplay（:161，`_36`）                   | DebuggerDisplay                                                |
' | MyException                                   | DebuggerDisplay（:161，`_36`）                   | DebuggerDisplay                                                |
' | ThrowingDictionary（E）                       | DebuggerProxy_FrameworkTypes_IDictionary（:518） | DebuggerProxy_FrameworkTypes_IDictionary(+_Exception)          |
' | ListNode                                      | Array_Recursive（:307）                          | Array_Recursive                                                |
' | LongMembers                                   | LongMembers（:366）                              | LongMembers                                                    |
'
' **夹具与 C# 侧的两条说明（不硬凑，给理由）**：
'   1. **改名规则（通用）**：VB 标识符大小写不敏感，夹具里与既有成员**仅大小写不同**的名字必须改名才能编译
'      （同类型重复声明落 `BC30260`，验证轮沙箱探针实测）。`Sort` 上有**两处**，不是一处：
'        `aB`（Custom.cs:50）→ `aB2`（ObjectFormatterFixtures.vb:28）；`ad`（Custom.cs:53）→ `ad2`（ObjectFormatterFixtures.vb:31）。
'      （`ab`/`aB` 在 VB 里是同一名字、`Ad`/`ad` 也是；改名后 `ab`+`aB2`、`Ad`+`ad2` 各自可共存。）
'      期望值随之从 C# 的 `aB=-1, ab=1, … , Ad=1, ad=-1` 变为 VB 的 `ab=1, aB2=-1, … , Ad=1, ad2=-1`
'      （成员顺序也随反射顺序变化）。既有 ObjectFormatterTests.vb:58 已按 VB 实际值断言。
'   2. **不是差异**：C# 夹具的 `readonly` 字段与各类属性在 VB 夹具里是**逐 1:1 的忠实映射**，无语义损失——
'      VB 有全部等价写法，不存在「VB 无法同时表达只读与 DebuggerDisplay」这回事：
'        C# get-only 属性（Custom.cs:145/148/151/154）→ VB `ReadOnly Property`（ObjectFormatterFixtures.vb:113/120/127/134）；
'        C# `readonly` 字段（Custom.cs:158-178）→ VB **`ReadOnly` 字段**（ObjectFormatterFixtures.vb:142-162）；
'        C# set-only 属性（Custom.cs:282）→ VB `WriteOnly Property`（ObjectFormatterFixtures.vb:300）；
'        该类型的其余成员同样逐一对应（get/set 组合、`_31`/`_36` 等，逐成员读两边文件核实）。
'      本文件断言的是 **VB 夹具** 的渲染结果，不是 C# 夹具的。
'
' **C# 有、本文件不移植的用例（各给理由，不造期望值）**：
'   - `Objects` / `ArrayMethodParameters`：既有 ObjectFormatterTests.vb:58 / :101 已覆盖同一夹具，本单元不重复。
'   - `DebuggerProxy_DiagnosticBag`（C# :842）：**充分理由是这条诊断在 VB 侧不存在**——断言正文里的 `CS0180`
'     （`ErrorCode.ERR_AbstractAndExtern`）在 VB 侧没有对应诊断（`grep -rn "ERR_AbstractAndExtern" Compilers\VisualBasic\`
'     **零命中**，检索模式不含 `<`，本轮复跑），无法 1:1 移植；用运行时消息现拼期望值会引入循环论证，故整格不移植。
'     （C# 基线另外靠 `Roslyn.Test.Utilities` 的 `EnsureEnglishUICulture` 把 UI culture 钉成英文，本仓测试项目不引用该包；
'     这一条**不是**关键理由：该类只赋 `CultureInfo.CurrentUICulture`、在 `Dispose()` 里还原
'     （`EnsureEnglishUICulture.cs:33-56`），UI culture 已是英文时 `PreferredOrNull` 返回 `null` ⇒ **整段 no-op**（`:15-27`），
'     且它不改进程默认的 `DefaultThreadCurrentUICulture`。「改 culture 是跨测试的进程级副作用」那种说法不成立。）
'   - `DebuggerProxy_FrameworkTypes_ConcurrentBag`：C# 侧本身就是 `//[Fact]` 注释掉的 TODO。
'
' **本文件自带夹具的落点与理由**（C# 夹具文件缺的三块，本文件按 C# 的分层方式补齐，形状逐字照搬）
'
' | 夹具 | C# 位置 | 本文件位置 | 为什么不能放别处 |
' |---|---|---|---|
' | `MockDesktopTask` / `MockTaskProxy` / `MockDesktopSpinLock` | `CoreTestUtilities\ObjectFormatterFixtures\MockDesktopTask.cs`、`MockDesktopSpinLock.cs`（独立文件，顶层） | `ScriptModeObjectFormatterFixtures.vb` 的 `Global.ObjectFormatterFixtures` 命名空间 | ① `Helpers\ObjectFormatterFixtures.vb` 是本单元的**只读输入**（本单元禁止改）；② 放进测试类内部会让类型名带上 `测试类名.` 前缀，与 C# 基线不可比 |
' | `CoreRangeIterator` / `RangeFixtures` / `IteratorHost`（含 `IteratorRange`） | `CSharpTest\ObjectFormatterTests.cs:458-496`（测试类内部） | 同上，顶层类 | 同上 ②（`FormatObject` 的 `showNamespaces:=False`，嵌套类型会印成 `外层类名.自身名`） |
' | `StackFixture` / `StackFixture(Of T)` / `ParameterStackFixture`（栈帧夹具） | `CSharpTest\ObjectFormatterTests.cs:898-926` 的 `#line` 区域 | 本文件末尾的 `#ExternalSource` 区域 | **必须与断言同文件**（实锤，探针两次读数）：某文件含 `#ExternalSource` 时，该文件里区域**外**的方法帧也没有真实文件信息（`GetFileName()` 为 `Nothing`）；把夹具搬到另一个文件后，本文件的测试方法帧反而带上了本机绝对路径，期望值不可移植 |
' | `DynamicStackFixture`（晚期绑定） | `CSharpTest\ObjectFormatterTests.cs:1023-1030` 的 `Fixture2` | `ScriptModeObjectFormatterFixtures.vb` 末尾的 `#ExternalSource` 区域 | 需要一个文件级的 `Option Strict Off`（本项目默认 `Option Strict On`，`Directory.Build.props:25`），而 `Option` 指令是**文件级**的 |
'
' ------------------------------------------------------------------------------------------------------------------
' C# 基线 51 个测试方法的逐条去处（**无遗漏**；本单元共 41 个 `[Fact]`，因为有几格把 C# 的同族方法并成一条）
'
' 表头约定（本表双向可查：C# 方法 → 本文件落点，本文件方法 → C# 锚点）：
'   `(:N)` 里的数字一律是 **C# 锚点** = `ObjectFormatterTests.cs` 里该方法的 `public void` 行号，可逐条 grep 复核。
'     这里**不写本文件行号**：文件头一增删它们就整表漂移（本表曾因文件头加长 20 行而整表偏移 20），而方法名随时可 grep 取现值。
'   左列简写规则：`DebuggerProxy_FrameworkTypes_` 前缀省略（`Array` 即 `DebuggerProxy_FrameworkTypes_Array`，依此类推）。
'   未标 `→` 的行 = 本文件有去前缀后的**同名**方法；标 `→` 的行 = 落点与 C# 不同（合并格 / 既有文件 / 明确不移植）。
'
'   1 Objects(:32) / 4 ArrayMethodParameters(:90)      → 既有 ObjectFormatterTests.vb:58 / :101（本文件不重复）
'   2 TupleType(:76)        3 ValueTupleType(:83)       5 ArrayOfInt32_NoMembers(:97)   6 RecursiveRootHidden(:107)
'   7 DebuggerDisplay_ParseSimpleMemberName(:119)      8 DebuggerDisplay(:161)         9 DebuggerDisplay_Inherited(:263)
'  10 DebuggerProxy_DebuggerDisplayAndProxy(:272)     11 DebuggerProxy_Recursive(:287) 12 Array_Recursive(:307)
'  13 LargeGraph(:337)                                14 LongMembers(:366)             16 MdArray(:395)
'  15 Array(:378)                                     17 IEnumerable_Core(:443)         18 IEnumerable_Framework(:477)
'  19 IEnumerable_Exception(:499)                     20 IDictionary(:518)             21 IDictionary_Exception(:537)
'  22 BitArray(:548)                                  23 Queue(:557) + 24 Stack(:569)
'      → DebuggerProxy_FrameworkTypes_QueueAndStack
'  25 Dictionary(:581) + 26 KeyValuePair(:600)        → DebuggerProxy_FrameworkTypes_DictionaryAndKeyValuePair
'  27 List(:609) + 28 LinkedList(:618)                → DebuggerProxy_FrameworkTypes_ListAndLinkedList
'  29 SortedList(:630) + 30 SortedDictionary(:648)    → DebuggerProxy_FrameworkTypes_SortedListAndSortedDictionary
'  31 HashSet(:662) + 32 SortedSet(:675)              → DebuggerProxy_FrameworkTypes_HashSetAndSortedSet
'  33 ConcurrentDictionary(:686) + 34 ConcurrentQueue(:697) + 35 ConcurrentStack(:709)
'      → DebuggerProxy_FrameworkTypes_ConcurrentCollections
'  36 BlockingCollection(:721) + 37 ReadOnlyCollection(:743)
'      → DebuggerProxy_FrameworkTypes_BlockingCollectionAndReadOnlyCollection
'  38 Lazy(:752)                                      39 Task(:788)                     43 DebuggerProxy_ArrayBuilder(:862)
'  40 SpinLock1(:810) + 41 SpinLock2(:826)            → DebuggerProxy_FrameworkTypes_SpinLock（C# 两格并成 VB 一格）
'  44 FormatConstructorSignature(:883)
'  45-51 StackTrace_*（7 格）：NonGeneric(:931) / GenericMethod(:954) / GenericType(:978) /
'      GenericMethodInGenericType(:1002) / Dynamic(:1034) 五格为同名落点；GenericRefParameter(:1099)
'      → StackTrace_GenericByRefParameter；RefOutParameters(:1074) → 拆成 StackTrace_ByRefParameters +
'      StackTrace_OutParameters（拆格理由见 `StackTrace_*` 区域抬头）。
'  42 DiagnosticBag(:842)                             → **不移植**（理由见上）
'  另加 VB 侧 1 格：StackTrace_NullException（C# 基线无此格）。
'  计数核对：50 个 C# 方法有去处 + 1 个明确不移植 = 51；41 个 VB `[Fact]` = 50 个 C# 方法的去处 + 1 个 VB 独有格。
'
' ------------------------------------------------------------------------------------------------------------------
' 无副作用声明
'   本文件只用内存 API：`TestVisualBasicObjectFormatter.FormatObject` / `FormatMethodSignature` / `FormatException`，
'   以及内存里构造的 BCL 集合与 `ThrowingDictionary`。不写文件、不起进程、不写注册表、不访问网络。
'   `#ExternalSource("z:\Fixture.vb", …)` 在**默认配置**下只把该区域的**调试文档名与行号**记进本程序集的 PDB，不读也不写那个路径。
'
' **维护告诫：`#ExternalSource` 的构建脆弱性（改构建配置前必读）**
'   本文件末尾区域（`#ExternalSource("z:\Fixture.vb", 10000)`）与 `ScriptModeObjectFormatterFixtures.vb`（同名文件、基址 `20000`）
'   都用了 `#ExternalSource`。VB 编译器在**有 embedded files** 时会把这些指令的目标收进 embedded 列表
'   （`Compilers\Core\Portable\CommandLine\CommonCompiler.cs:416` 的空判定 → `:442` 的逐树调用 →
'   `Compilers\VisualBasic\Portable\CommandLine\VisualBasicCompiler.vb:286-295`），解析不出目标就在指令处报
'   `ERR_FileNotFound`（= **BC2001**，`Compilers\VisualBasic\Portable\Errors\Errors.vb:37`）；
'   而 `z:\Fixture.vb` 只在测试运行期有映射，盘上并不存在。
'   ⇒ 一旦这两个文件进入 embedded-sources 遍历（如 `-p:EmbedAllSources=true`），构建以 **BC2001 失败**（本单元实测：2 个 BC2001）。复现要带 `-t:Rebuild`：**该告诫的前提是树此前成功构建过一次、产物齐备**——此时不带它会被增量构建判定「已是最新」而跳过编译，看不到错误；产物已被上一次失败的重建清掉时，同样命令仍会重新编译并报错（状态相关）。
'   默认配置下它们不在 `EmbeddedFiles` 里，故常规构建与全量测试不受影响。C# 侧同构（同一调用点 `Compilers\Core\Portable\CommandLine\CommonCompiler.cs:442`）：`Compilers\CSharp\Portable\CommandLine\CSharpCompiler.cs:344-374` 对 `#line` 目标同样调 `resolver.ResolveReference`，解析不出同样报 `ERR_NoSourceFile`（= **CS1504**，`Compilers\CSharp\Portable\Errors\ErrorCode.cs:685`）；与 VB 的差异只在指令形态（VB 区域式 / C# 到文件末）与诊断码（BC2001 vs CS1504），不在「要不要解析」。故一旦进 embedded 遍历，C# 基线同样失败。
'   改动构建配置（`EmbeddedFiles` / `EmbedAllSources` / Source Link 相关）后，先跑一次常规构建确认，再跑全量测试。
' ==================================================================================================================

Imports System
Imports System.Collections
Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Collections.ObjectModel
Imports System.Diagnostics
Imports System.Linq
Imports System.Reflection
Imports System.Runtime.CompilerServices
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.PooledObjects
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports ObjectFormatterFixtures
Imports Xunit

Public Class ScriptModeObjectFormatterTests
    Inherits ObjectFormatterTestBase

    Private Shared ReadOnly s_formatter As ObjectFormatter = New TestVisualBasicObjectFormatter()

    ''' <summary>
    ''' C# 基线的 `#line 10000 "z:\Fixture.cs"` 对应的 VB 写法。`#ExternalSource` 的好处与 C# 的 `#line` 一样：
    ''' 栈帧里的文件与行号不随本文件上面的行数变动而漂移，期望值不必每次改文件都重算。
    ''' 与 C# 的差别（实锤）：C# 的 `#line` 一直管到文件末尾，所以基线里**两个**帧都带 ` at …`；VB 的 `#ExternalSource`
    ''' 只覆盖被标记的区域，故只有夹具那一帧带 ` at z:\Fixture.vb : N`，调用它的测试方法帧不带（无文件信息）。
    ''' </summary>
    Private Const StackDocPath As String = "z:\Fixture.vb"

    ''' <summary>
    ''' 泛型类型的帧签名（实锤，见 `StackTrace_GenericType` 的注释）：共享的 `CommonTypeNameFormatter.FormatGenericTypeName`
    ''' （`Scripting\Core\Hosting\ObjectFormatter\CommonTypeNameFormatter.cs:272-276`）**只在 `DeclaringType IsNot Nothing`
    ''' 时**才补命名空间（`:251-258`）——顶层泛型类型走 `:273-276` 的 else 分支，于是 `showNamespaces:=True` 对它失效，
    ''' 印出来是 `StackFixture(Of T)` 而**不含命名空间**；同一次调用里的非泛型类型走 `FormatNonGenericTypeName`
    ''' （`:65-70`）则带上完整命名空间。本值就是这条不对称的钉点（C# 基线因为把夹具嵌套在测试类里，走的是带命名空间那一支）。
    ''' **本值是特征钉死（characterization pin），不是正确性 oracle**：它钉的是「共享实现眼下长这样」，不是「这样是对的」——
    ''' 顶层泛型丢命名空间是本 fork 与上游共有的缺陷（登记于 `InternalDevDocs\issues\issue-generic-type-name-loses-namespace.md`）。
    ''' 上游若修掉这条不对称，本格**应当**失败，届时按新行为同步期望值，不要为保住绿而放宽断言。
    ''' </summary>
    Private Shared ReadOnly StackFixtureGenericName As String = "StackFixture(Of T)"

    Private Shared Function AtFileLine(lineNumber As Integer) As String
        ' `ScriptingResources.AtFileLine` 是本地化资源（本机 UI culture 下为 " 在 {0} : {1}"），
        ' 基线用同一个资源拼期望值，故断言在任何 UI culture 下都成立。
        Return String.Format(ScriptingResources.AtFileLine, StackDocPath, lineNumber)
    End Function

    Private Shared Function NL() As String
        Return Environment.NewLine
    End Function

#Region "DebuggerDisplay / DebuggerProxy（夹具类形状）"

    ''' <summary>
    ''' C# 基线 `DebuggerDisplay_ParseSimpleMemberName`（ObjectFormatterTests.cs:119；被调 helper 在 :146）。
    ''' 夹具：无（被测的是 `ObjectFormatterHelpers.ParseSimpleMemberName`，纯解析）。
    ''' 判别力：期望值是每个输入的 (name, callable, nq) 三元组；把解析器的任何一条分支撤销——
    '''   例如「`nq` 后缀要先剥空白再判定」——`"  ,nq"` 与 `"goo,  nq"` 两组就会返回不同的名字或标志，
    '''   而 `"goo(,nq"`/`"goo),nq"` 这两组专门钉「不成对的圆括号不构成 callable，且括号留在名字里」。
    ''' </summary>
    <Fact>
    Public Sub DebuggerDisplay_ParseSimpleMemberName()
        Test_ParseSimpleMemberName("goo", name:="goo", callable:=False, nq:=False)
        Test_ParseSimpleMemberName("goo  ", name:="goo", callable:=False, nq:=False)
        Test_ParseSimpleMemberName("   goo", name:="goo", callable:=False, nq:=False)
        Test_ParseSimpleMemberName("   goo   ", name:="goo", callable:=False, nq:=False)

        Test_ParseSimpleMemberName("goo()", name:="goo", callable:=True, nq:=False)
        Test_ParseSimpleMemberName(vbLf & "goo (" & vbCrLf & ")", name:="goo", callable:=True, nq:=False)
        Test_ParseSimpleMemberName(" goo ( " & vbTab & ") ", name:="goo", callable:=True, nq:=False)

        Test_ParseSimpleMemberName("goo,nq", name:="goo", callable:=False, nq:=True)
        Test_ParseSimpleMemberName("goo  ,nq", name:="goo", callable:=False, nq:=True)
        Test_ParseSimpleMemberName("goo(),nq", name:="goo", callable:=True, nq:=True)
        Test_ParseSimpleMemberName("  goo " & vbTab & "( )   ,nq", name:="goo", callable:=True, nq:=True)
        Test_ParseSimpleMemberName("  goo " & vbTab & "( )   , nq", name:="goo", callable:=True, nq:=True)

        Test_ParseSimpleMemberName("goo,  nq", name:="goo", callable:=False, nq:=True)
        Test_ParseSimpleMemberName("goo(,nq", name:="goo(", callable:=False, nq:=True)
        Test_ParseSimpleMemberName("goo),nq", name:="goo)", callable:=False, nq:=True)
        Test_ParseSimpleMemberName("goo ( ,nq", name:="goo (", callable:=False, nq:=True)
        Test_ParseSimpleMemberName("goo ) ,nq", name:="goo )", callable:=False, nq:=True)

        Test_ParseSimpleMemberName(",nq", name:="", callable:=False, nq:=True)
        Test_ParseSimpleMemberName("  ,nq", name:="", callable:=False, nq:=True)
    End Sub

    ''' <summary>与 C# 同名 helper（ObjectFormatterTests.cs:146 `Test_ParseSimpleMemberName`）逐字同形。</summary>
    Private Shared Sub Test_ParseSimpleMemberName(value As String, name As String, callable As Boolean, nq As Boolean)
        Dim actualNoQuotes As Boolean
        Dim actualIsCallable As Boolean
        Dim actualName As String = ObjectFormatterHelpers.ParseSimpleMemberName(value, 0, value.Length, actualNoQuotes, actualIsCallable)
        Assert.Equal(name, actualName)
        Assert.Equal(nq, actualNoQuotes)
        Assert.Equal(callable, actualIsCallable)

        actualName = ObjectFormatterHelpers.ParseSimpleMemberName("---" & value & "-", 3, 3 + value.Length, actualNoQuotes, actualIsCallable)
        Assert.Equal(name, actualName)
        Assert.Equal(nq, actualNoQuotes)
        Assert.Equal(callable, actualIsCallable)
    End Sub

    ''' <summary>
    ''' C# 基线 `DebuggerDisplay`（ObjectFormatterTests.cs:161）第一段（`ComplexProxy`）+ 第二段（`TypeWithComplexProxy`）。
    ''' 夹具：`ComplexProxy`/`ComplexProxyBase`/`TypeWithComplexProxy`。
    ''' 判别力：这张成员表同时钉住五件互相独立的事，撤销任何一件都会让某一行对不上——
    '''   (1) `<DebuggerDisplay>` 的文本优先于值本身（`_02…*1` 而不是 `0`）；
    '''   (2) `<DebuggerBrowsable(Never)>` 抑制整行（`_06_public_field_dd_never`/`_37` 不出现）；
    '''   (3) `<DebuggerBrowsable(RootHidden)>` 把成员摊平（`_27_rootHidden` 不出现，其内容 `A: 1 / B: 2` 以 `A`/`B` 出现在表里）；
    '''   (4) 嵌入表达式的花括号转义，含 `{{`、`{}` 注释形式与 `nq` 后缀（`_17_braces_0..6`、`_21`…`_25`）；
    '''   (5) 成员求值抛异常时的降级（`_34_Exception: !<Exception>`、`_35_Exception` 走 DD 的 `-!-`、`_36: !<MyException>`）。
    ''' 撤销 (3) 会让 `A: 1`/`B: 2` 两行消失并多出 `_27_rootHidden`；撤销 (5) 会让三行 `!<…>` 变成真实值或异常外泄。
    ''' 第二段（`TypeWithComplexProxy` 以 `ComplexProxy` 为代理）行数更少：代理视图只看得到 `ComplexProxy` 的可访问成员，
    '''   `_03/_05/_07/_09/_13/_15/_33/_40` 依赖的是「代理 + 非公有成员过滤」这条规则，撤销它这些行会重新出现。
    ''' </summary>
    <Fact>
    Public Sub DebuggerDisplay()
        Dim str As String
        Dim a = New ComplexProxy()

        str = s_formatter.FormatObject(a, SeparateLinesOptions)
        AssertMembers(str, "[AStr]",
            "_02_public_property_dd: *1",
            "_03_private_property_dd: *2",
            "_04_protected_property_dd: *3",
            "_05_internal_property_dd: *4",
            "_07_private_field_dd: +2",
            "_08_protected_field_dd: +3",
            "_09_internal_field_dd: +4",
            "_10_private_collapsed: 0",
            "_12_public: 0",
            "_13_private: 0",
            "_14_protected: 0",
            "_15_internal: 0",
            "_16_eolns: ==" & NL() & "=" & NL() & "=",
            "_17_braces_0: =={==",
            "_17_braces_1: =={{==",
            "_17_braces_2: ==!<Member ''{'' not found>==",
            "_17_braces_3: ==!<Member ''\{'' not found>==",
            "_17_braces_4: ==!<Member '1/*{*/' not found>==",
            "_17_braces_5: ==!<Member ''{'/*\' not found>*/}==",
            "_17_braces_6: ==!<Member ''{'/*' not found>*/}==",
            "_19_escapes: ==\{\x\t==",
            "_21: !<Member '1+1' not found>",
            "_22: !<Member '""xxx""' not found>",
            "_23: !<Member '""xxx""' not found>",
            "_24: !<Member ''x'' not found>",
            "_25: !<Member ''x'' not found>",
            "_26_0: !<Method 'new B' not found>",
            "_26_1: !<Method 'new D' not found>",
            "_26_2: !<Method 'new E' not found>",
            "_26_3: ",
            "_26_4: !<Member 'F1(1)' not found>",
            "_26_5: 1",
            "_26_6: 2",
            "A: 1",
            "B: 2",
            "_28: [CStr]",
            "_29_collapsed: [CStr]",
            "_31: 0",
            "_32: 0",
            "_33: 0",
            "_34_Exception: !<Exception>",
            "_35_Exception: -!-",
            "_36: !<MyException>",
            "_38_private_get_public_set: 1",
            "_39_public_get_private_set: 1",
            "_40_private_get_private_set: 1")

        Dim b = New TypeWithComplexProxy()
        str = s_formatter.FormatObject(b, SeparateLinesOptions)

        AssertMembers(str, "[BStr]",
            "_02_public_property_dd: *1",
            "_04_protected_property_dd: *3",
            "_08_protected_field_dd: +3",
            "_10_private_collapsed: 0",
            "_12_public: 0",
            "_14_protected: 0",
            "_16_eolns: ==" & NL() & "=" & NL() & "=",
            "_17_braces_0: =={==",
            "_17_braces_1: =={{==",
            "_17_braces_2: ==!<Member ''{'' not found>==",
            "_17_braces_3: ==!<Member ''\{'' not found>==",
            "_17_braces_4: ==!<Member '1/*{*/' not found>==",
            "_17_braces_5: ==!<Member ''{'/*\' not found>*/}==",
            "_17_braces_6: ==!<Member ''{'/*' not found>*/}==",
            "_19_escapes: ==\{\x\t==",
            "_21: !<Member '1+1' not found>",
            "_22: !<Member '""xxx""' not found>",
            "_23: !<Member '""xxx""' not found>",
            "_24: !<Member ''x'' not found>",
            "_25: !<Member ''x'' not found>",
            "_26_0: !<Method 'new B' not found>",
            "_26_1: !<Method 'new D' not found>",
            "_26_2: !<Method 'new E' not found>",
            "_26_3: ",
            "_26_4: !<Member 'F1(1)' not found>",
            "_26_5: 1",
            "_26_6: 2",
            "A: 1",
            "B: 2",
            "_28: [CStr]",
            "_29_collapsed: [CStr]",
            "_31: 0",
            "_32: 0",
            "_34_Exception: !<Exception>",
            "_35_Exception: -!-",
            "_36: !<MyException>",
            "_38_private_get_public_set: 1",
            "_39_public_get_private_set: 1")
    End Sub

    ''' <summary>
    ''' C# 基线 `DebuggerDisplay_Inherited`（ObjectFormatterTests.cs:263）。夹具：`InheritedDebuggerDisplay` / `BaseClassWithDebuggerDisplay`。
    ''' 判别力：`<DebuggerDisplay>` 在**基类**上，派生类自身没有该属性。期望值 `InheritedDebuggerDisplay(DebuggerDisplayValue)`
    '''   同时要求「沿继承链上溯找到特性」（撤销上溯 ⇒ 输出退化成 `InheritedDebuggerDisplay`）与「显示的是**派生**类名」
    '''   （撤销 ⇒ 会印成 `BaseClassWithDebuggerDisplay(...)`）。
    ''' </summary>
    <Fact>
    Public Sub DebuggerDisplay_Inherited()
        Dim obj = New InheritedDebuggerDisplay()
        Assert.Equal("InheritedDebuggerDisplay(DebuggerDisplayValue)", s_formatter.FormatObject(obj, SingleLineOptions))
    End Sub

    ''' <summary>
    ''' C# 基线 `DebuggerProxy_DebuggerDisplayAndProxy`（ObjectFormatterTests.cs:272）。夹具：`TypeWithDebuggerDisplayAndProxy`（+ 嵌套 `Proxy`）。
    ''' 判别力：同一个类型同时带 `<DebuggerDisplay("DD")>` 与 `<DebuggerTypeProxy>`，DD 决定**头部**、代理决定**成员表**。
    '''   撤销代理 ⇒ 成员表会是类型自己的成员（空表，因为该类型没有可显示成员）；撤销 DD ⇒ 头部变成 `<ToString>`（该类型重写了 ToString）。
    '''   两条断言各钉一侧：SingleLine 一行内同时含 `(DD)` 与 `{ A=0, B=0 }`，SeparateLines 是两行成员表。
    ''' </summary>
    <Fact>
    Public Sub DebuggerProxy_DebuggerDisplayAndProxy()
        Dim obj = New TypeWithDebuggerDisplayAndProxy()

        Assert.Equal("TypeWithDebuggerDisplayAndProxy(DD) { A=0, B=0 }", s_formatter.FormatObject(obj, SingleLineOptions))

        AssertMembers(s_formatter.FormatObject(obj, SeparateLinesOptions), "TypeWithDebuggerDisplayAndProxy(DD)",
            "A: 0",
            "B: 0")
    End Sub

    ''' <summary>
    ''' C# 基线 `DebuggerProxy_Recursive`（ObjectFormatterTests.cs:287）。夹具：`RecursiveProxy` / `InvalidRecursiveProxy`。
    ''' 判别力：两段各钉一个机制。
    '''   第一段：`RecursiveProxy.Node` 的代理把 `value`/`next` 投影成 `x`/`y`，链表 0→5；期望值把嵌套的**深度与断点**都写死
    '''     （第 5 个节点后 `y=Nothing`，因为夹具只在 value &lt; 5 时建 next）。撤销 «代理参与渲染» ⇒ 输出退化成 `value: 0, next: …`。
    '''   第二段：`InvalidRecursiveProxy` 的代理指向自身（`Node` 的代理有 `Node p` 字段），渲染必然爆栈；期望值要求把它**降级成资源文本**
    '''     而不是把异常抛出去。撤销 visitor 的 `InsufficientExecutionStackException` 兜底 ⇒ 这条用例会以异常/断言失败结束。
    '''   资源用 `ScriptingResources.StackOverflowWhileEvaluating`（本地化），故断言不绑定英文。
    ''' </summary>
    <Fact>
    Public Sub DebuggerProxy_Recursive()
        Dim obj As Object = New RecursiveProxy.Node(0)

        AssertMembers(s_formatter.FormatObject(obj, SeparateLinesOptions), "RecursiveProxy.Node",
            "x: 0",
            "y: RecursiveProxy.Node { x=1, y=RecursiveProxy.Node { x=2, y=RecursiveProxy.Node { x=3, y=RecursiveProxy.Node { x=4, y=RecursiveProxy.Node { x=5, y=Nothing } } } } }")

        obj = New InvalidRecursiveProxy.Node()
        Assert.Equal(ScriptingResources.StackOverflowWhileEvaluating, s_formatter.FormatObject(obj, SeparateLinesOptions))
    End Sub

    ''' <summary>
    ''' C# 基线 `RecursiveRootHidden`（ObjectFormatterTests.cs:107）。夹具：`RecursiveRootHidden`。
    ''' 判别力：夹具把 C 指向自己，`C` 带 `<DebuggerBrowsable(RootHidden)>`。期望值 `RecursiveRootHidden { A=0, B=0 }` 要求
    '''   (1) RootHidden 成员被摊平，(2) 摊平后仍能识别出「C 就是根对象」从而不展开——撤销 (2) 会无限展开，
    '''   撤销 (1) 会印出 `C: …`。C# 基线原文里带 `DO_NOT_ADD_TO_WATCH_WINDOW` 的警告，此处保留同名变量以示同源。
    ''' </summary>
    <Fact>
    Public Sub RecursiveRootHidden()
        Dim DO_NOT_ADD_TO_WATCH_WINDOW = New ObjectFormatterFixtures.RecursiveRootHidden()
        DO_NOT_ADD_TO_WATCH_WINDOW.C = DO_NOT_ADD_TO_WATCH_WINDOW

        Assert.Equal("RecursiveRootHidden { A=0, B=0 }", s_formatter.FormatObject(DO_NOT_ADD_TO_WATCH_WINDOW, SingleLineOptions))
    End Sub

#End Region

#Region "数组、元组、长度限制"

    ''' <summary>
    ''' C# 基线 `TupleType`（ObjectFormatterTests.cs:76）。夹具：无。
    ''' 判别力：`Tuple(Of Integer, Integer)` 的渲染必须走「元组特判」而不是成员表。撤销特判 ⇒ 输出会是 `Tuple(Of Integer, Integer) { … }`。
    ''' </summary>
    <Fact>
    Public Sub TupleType()
        Dim tup = New Tuple(Of Integer, Integer)(1, 2)
        Assert.Equal("(1, 2)", s_formatter.FormatObject(tup, SingleLineOptions))
    End Sub

    ''' <summary>
    ''' C# 基线 `ValueTupleType`（ObjectFormatterTests.cs:83）。夹具：无。
    ''' 判别力：与 `TupleType` 分开一格，因为 `ValueTuple` 有独立的识别分支（`IsTuple` 走 `ITuple`）；撤销该分支 ⇒ 输出变成 `(1, 2)` 之外的成员表形式。
    ''' </summary>
    <Fact>
    Public Sub ValueTupleType()
        Dim tup = (1, 2)
        Assert.Equal("(1, 2)", s_formatter.FormatObject(tup, SingleLineOptions))
    End Sub

    ''' <summary>
    ''' C# 基线 `ArrayOfInt32_NoMembers`（ObjectFormatterTests.cs:97）。夹具：无。
    ''' 判别力：`Hidden` 模式下集合**仍要**展开元素（`FormatObjectRecursive` 里 `MemberDisplayFormat.Hidden` 对 ICollection 特判），
    '''   非数组对象在 Hidden 下只印类型名。期望值 `Integer(4) { 3, 4, 5, 6 }` 撤销该特判就会退化成 `Integer()`。
    '''   同时钉住 VB 的类型名拼写 `Integer(4)`（C# 基线是 `int[4]`）。
    ''' </summary>
    <Fact>
    Public Sub ArrayOfInt32_NoMembers()
        Dim o As Object = New Integer() {3, 4, 5, 6}
        Assert.Equal("Integer(4) { 3, 4, 5, 6 }", s_formatter.FormatObject(o, HiddenOptions))
    End Sub

    ''' <summary>
    ''' C# 基线 `Array_Recursive`（ObjectFormatterTests.cs:307）。夹具：`ListNode`。
    ''' 判别力：这张图同时含自引用数组、互相引用的 `ListNode`、以及「已经访问过」的节点，期望值把三件事全写死——
    '''   (1) 重复出现的对象渲染成 `{ ... }`（撤销 ⇒ 无限递归/爆栈）；
    '''   (2) 首次出现的嵌套对象按 `SingleLine` 渲染（`SeparateLines` 只作用于根）；
    '''   (3) `Nothing` 成员照常显示（`ListNode { data=Nothing, next=Nothing }`）。
    '''   SeparateLines 与 SingleLine 两条断言必须**同时**成立：只留一条会漏掉 (2)。
    ''' </summary>
    <Fact>
    Public Sub Array_Recursive()
        Dim n2 As ListNode
        Dim n1 As New ListNode()
        Dim obj As Object() = New Object(4) {}

        obj(0) = 1
        obj(1) = obj
        obj(2) = New ListNode() With {.data = obj, .next = n1}
        n2 = DirectCast(obj(2), ListNode)
        obj(3) = New Object() {4, 5, obj, 6, New ListNode()}
        obj(4) = 3
        n1.next = n2
        n1.data = New Object() {7, n2, 8, obj}

        AssertMembers(s_formatter.FormatObject(obj, SeparateLinesOptions), "Object(5)",
            "1",
            "{ ... }",
            "ListNode { data={ ... }, next=ListNode { data=Object(4) { 7, ListNode { ... }, 8, { ... } }, next=ListNode { ... } } }",
            "Object(5) { 4, 5, { ... }, 6, ListNode { data=Nothing, next=Nothing } }",
            "3")

        Assert.Equal("Object(5) { 1, { ... }, ListNode { data={ ... }, next=ListNode { data=Object(4) { 7, ListNode { ... }, 8, { ... } }, next=ListNode { ... } } }, Object(5) { 4, 5, { ... }, 6, ListNode { data=Nothing, next=Nothing } }, 3 }",
                     s_formatter.FormatObject(obj, SingleLineOptions))
    End Sub

    ''' <summary>
    ''' C# 基线 `LargeGraph`（ObjectFormatterTests.cs:337）。夹具：无（现场建 10000 层嵌套 `LinkedList(Of Object)`）。
    ''' 判别力：对 `MaximumOutputLength` 从 100 递减到 5 逐档断言「输出长度恰为 i + 3」且前缀逐字等于基线前缀 + `...`。
    '''   撤销截断（不再按 `MaximumOutputLength` 裁剪）⇒ 输出会长于 i + 3；撤销省略号 ⇒ 长度少 3。
    '''   这一格同时是**深度**压力：图很深但不宽，故不会撞 `EnsureSufficientExecutionStack` 的溢出降级路径。
    ''' </summary>
    <Fact>
    Public Sub LargeGraph()
        Dim list As New LinkedList(Of Object)()
        Dim obj As Object = list
        For i = 0 To 9999
            Dim node = list.AddFirst(i)
            Dim newList As New LinkedList(Of Object)()
            list.AddAfter(node, newList)
            list = newList
        Next

        Dim output = "LinkedList(Of Object)(2) { 0, LinkedList(Of Object)(2) { 1, LinkedList(Of Object)(2) { 2, LinkedList(Of Object)(2) {"

        For i = 100 To 5 Step -1
            Dim printOptions As New PrintOptions With {
                .MaximumOutputLength = i,
                .MemberDisplayFormat = MemberDisplayFormat.SingleLine
            }

            Dim actual = s_formatter.FormatObject(obj, printOptions)
            Assert.Equal(output.Substring(0, i) & "...", actual)
        Next
    End Sub

    ''' <summary>
    ''' C# 基线 `LongMembers`（ObjectFormatterTests.cs:366）。夹具：`LongMembers`。
    ''' 判别力：`maximumLineLength := 20` 下的两种模式各一条：SingleLine 把整行截到 `LongMembers { LongNa...`；
    '''   SeparateLines 是**逐成员**截断（成员名截成 `LongName0123456789...`、字符串值截成 `"012345...`），
    '''   且不补省略号后缀以外的内容。撤销行宽限制 ⇒ 两条都退化成完整值；把两种模式的截断口径搞混（例如 SeparateLines 也走整行截断）
    '''   ⇒ 第二条会变成一行。
    ''' </summary>
    <Fact>
    Public Sub LongMembers()
        Dim obj As Object = New LongMembers()
        Dim formatter = New TestVisualBasicObjectFormatter(maximumLineLength:=20)

        Assert.Equal("LongMembers { LongNa...", formatter.FormatObject(obj, SingleLineOptions))

        Assert.Equal("LongMembers {" & NL() & "  LongName0123456789..." & NL() & "  LongValue: ""012345..." & NL() & "}" & NL(),
                     formatter.FormatObject(obj, SeparateLinesOptions))
    End Sub

    ''' <summary>
    ''' C# 基线 `FormatConstructorSignature`（ObjectFormatterTests.cs:883）。夹具：无（反射 `Object` 的唯一构造器）。
    ''' 判别力：期望值 `Object..ctor()` 是**两个**拼接错误都会改的字符串——声明类型名（VB 拼写 `Object`，C# 基线是 `object`）
    '''   与 `.` 与 `..ctor` 之间那条分隔（少一个点会变成 `Object.ctor()`）。C# 基线自己也只把它当「不抛异常」的探针用，
    '''   本格按 VB 实测把它钉成逐字值。
    ''' </summary>
    <Fact>
    Public Sub FormatConstructorSignature()
        Dim constructor = GetType(Object).GetTypeInfo().DeclaredConstructors.Single()
        Dim signature = DirectCast(s_formatter, CommonObjectFormatter).FormatMethodSignature(constructor)
        Assert.Equal("Object..ctor()", signature)
    End Sub

    ''' <summary>
    ''' C# 基线 `DebuggerProxy_FrameworkTypes_MdArray`（ObjectFormatterTests.cs:395）。夹具：无。
    ''' 判别力：四个子形状各钉一件事——
    '''   (1) `Integer(2, 3, 4)` 的三维展开（撤销维度递归 ⇒ 只剩一层花括号）；
    '''   (2) 交错数组 `Integer()()(,)(,,,)` 的「元素为 Nothing 时印 Nothing」（C# 是 `null`）；
    '''   (3) 非零下界 `Object(2..4, 9..12)`：上界=下界+长度-1，撤销下界计算会印成 `Object(2, 3)` 或 `Object(0..2, 0..2)`；
    '''   (4) 空维度 `Object(0, 0) { }`：边界情形，撤销 ⇒ 抛异常或印出多余元素。
    ''' </summary>
    <Fact>
    Public Sub DebuggerProxy_FrameworkTypes_MdArray()
        Dim a(1, 2, 3) As Integer
        a(0, 0, 0) = 0 : a(0, 0, 1) = 1 : a(0, 0, 2) = 2 : a(0, 0, 3) = 3
        a(0, 1, 0) = 10 : a(0, 1, 1) = 11 : a(0, 1, 2) = 12 : a(0, 1, 3) = 13
        a(0, 2, 0) = 20 : a(0, 2, 1) = 21 : a(0, 2, 2) = 22 : a(0, 2, 3) = 23
        a(1, 0, 0) = 100 : a(1, 0, 1) = 101 : a(1, 0, 2) = 102 : a(1, 0, 3) = 103
        a(1, 1, 0) = 110 : a(1, 1, 1) = 111 : a(1, 1, 2) = 112 : a(1, 1, 3) = 113
        a(1, 2, 0) = 120 : a(1, 2, 1) = 121 : a(1, 2, 2) = 122 : a(1, 2, 3) = 123

        Assert.Equal("Integer(2, 3, 4) { { { 0, 1, 2, 3 }, { 10, 11, 12, 13 }, { 20, 21, 22, 23 } }, { { 100, 101, 102, 103 }, { 110, 111, 112, 113 }, { 120, 121, 122, 123 } } }",
                     s_formatter.FormatObject(a, SingleLineOptions))

        AssertMembers(s_formatter.FormatObject(a, SeparateLinesOptions), "Integer(2, 3, 4)",
            "{ { 0, 1, 2, 3 }, { 10, 11, 12, 13 }, { 20, 21, 22, 23 } }",
            "{ { 100, 101, 102, 103 }, { 110, 111, 112, 113 }, { 120, 121, 122, 123 } }")

        Dim jagged(1)(,)(,,,) As Integer
        jagged(0) = New Integer(0, 1)(,,,) {}
        jagged(0)(0, 0) = New Integer(0, 1, 2, 3) {}
        Assert.Equal("Integer(2)(,)(,,,) { Integer(1, 2)(,,,) { { Integer(1, 2, 3, 4) { { { { 0, 0, 0, 0 }, { 0, 0, 0, 0 }, { 0, 0, 0, 0 } }, { { 0, 0, 0, 0 }, { 0, 0, 0, 0 }, { 0, 0, 0, 0 } } } }, Nothing } }, Nothing }",
                     s_formatter.FormatObject(jagged, SingleLineOptions))

        Dim x = Array.CreateInstance(GetType(Object), New Integer() {2, 3}, New Integer() {2, 9})
        Assert.Equal("Object(2..4, 9..12) { { Nothing, Nothing, Nothing }, { Nothing, Nothing, Nothing } }",
                     s_formatter.FormatObject(x, SingleLineOptions))

        Dim y = Array.CreateInstance(GetType(Object), New Integer() {1, 1}, New Integer() {0, 0})
        Assert.Equal("Object(1, 1) { { Nothing } }", s_formatter.FormatObject(y, SingleLineOptions))

        Dim z = Array.CreateInstance(GetType(Object), New Integer() {0, 0}, New Integer() {0, 0})
        Assert.Equal("Object(0, 0) { }", s_formatter.FormatObject(z, SingleLineOptions))
    End Sub

#End Region

#Region "框架类型代理（DebuggerProxy_FrameworkTypes_*）"

    ''' <summary>
    ''' C# 基线 `DebuggerProxy_FrameworkTypes_Array`（ObjectFormatterTests.cs:378）。夹具：`C`（+ BCL 数组/字符/布尔）。
    ''' 判别力：期望值把 7 个元素的渲染各写死一格：重写 ToString 的对象走 `[CStr]`、`1` 是数字字面量、`"s"c` 是 **VB** 字符字面量
    '''   （C# 基线是 `'c'`）、`True`/`Nothing` 是 VB 拼写、嵌套 `Boolean(4)` 数组递归展开。撤销「重写 ToString 就走 ToString」
    '''   ⇒ 第一格会变成 `C { A=1, B=2 }`。
    ''' </summary>
    <Fact>
    Public Sub DebuggerProxy_FrameworkTypes_Array()
        Dim obj As Object() = New Object() {New C(), 1, "s"c, True, Nothing, New Boolean() {True, False, True, False}}
        AssertMembers(s_formatter.FormatObject(obj, SeparateLinesOptions), "Object(6)",
            "[CStr]",
            "1",
            """s""c",
            "True",
            "Nothing",
            "Boolean(4) { True, False, True, False }")
    End Sub

    ''' <summary>
    ''' C# 基线 `DebuggerProxy_FrameworkTypes_IEnumerable_Core`（ObjectFormatterTests.cs:443）。夹具：`CoreRangeIterator`
    ''' （在 ScriptModeObjectFormatterFixtures.vb，C# 基线把它放在测试类里，见 ObjectFormatterTests.cs:458-474）。
    ''' 判别力：该类型实现 `IEnumerable(Of Integer)` 但**不实现** `ICollection`，故头部来自 `<DebuggerDisplay("Count = {CountForDebugger}")>`
    '''   而不是 `ICollection.Count` 的 `(n)` 形式。期望值 `CoreRangeIterator(Count = 10)` 撤销 DD 分支 ⇒ 退化成类型名；把 `ICollection`
    '''   分支当成也能命中 ⇒ 会印成 `(10)`。`CountForDebugger` 是**非公有**属性，故这一格同时钉住「DD 嵌入表达式能取到私有成员」。
    ''' </summary>
    <Fact>
    Public Sub DebuggerProxy_FrameworkTypes_IEnumerable_Core()
        Dim str = s_formatter.FormatObject(RangeFixtures.Range_Core(0, 10), SingleLineOptions)
        Assert.Equal("CoreRangeIterator(Count = 10)", str)
    End Sub

    ''' <summary>
    ''' C# 基线 `DebuggerProxy_FrameworkTypes_IEnumerable_Framework`（ObjectFormatterTests.cs:477）。夹具：本文件内的 VB 迭代器
    ''' `IteratorRange`（C# 基线同样把它定义在测试类里，见 ObjectFormatterTests.cs:492-496）。
    ''' 判别力：迭代器方法编译成状态机类型，本格断言的是**该状态机类型名**加序列元素。C# 的
    '''   `CSharpTypeNameFormatter.FormatTypeName`（`CSharpTypeNameFormatter.cs:52-61`）会把生成名还原成源方法名（`RangeIterator`）；
    '''   VB 的对应实现**有意不做**这件事——`VisualBasicTypeNameFormatter.vb:81-85` 的 `FormatTypeName` 直接转发基类，
    '''   并在 `:82` 挂着 `TODO (https://github.com/dotnet/roslyn/issues/3739): handle generated type names`。
    '''   所以 VB 实际印出的是编译器生成名 `IteratorHost.VB$StateMachine_&lt;n&gt;_IteratorRange`，本条按 VB 实际行为断言
    '''   （这是与 C# 的真实差异，不是缺陷）。
    '''   注意：`&lt;n&gt;` 是编译器给 `IteratorHost` 里合成类型编的序号，**该类的成员一旦增删就要同步改这里的 n**
    '''   （踩坑点，故夹具独占一个顶层类；实测只有 `Range_Framework` + `IteratorRange` 两个成员时该值为 2）。
    ''' </summary>
    <Fact>
    Public Sub DebuggerProxy_FrameworkTypes_IEnumerable_Framework()
        Dim str = s_formatter.FormatObject(IteratorHost.Range_Framework(0, 10), SingleLineOptions)
        Assert.Equal("IteratorHost.VB$StateMachine_2_IteratorRange { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }", str)
    End Sub

    ''' <summary>
    ''' C# 基线 `DebuggerProxy_FrameworkTypes_IEnumerable_Exception`（ObjectFormatterTests.cs:499）。夹具：无（BCL 的 `Where`）。
    ''' 判别力：枚举到第 5 个元素时抛异常；期望值要求「已产出的 5 个元素照常印出 + 用 `!<Exception>` 收尾 + 省略号」。
    '''   撤销异常降级 ⇒ 异常外泄、用例以非断言失败结束；撤销省略号 ⇒ 少一个 ` ...`。
    '''   迭代器类型名按 **CoreCLR 9+** 实测写死：C# 基线用 `RuntimeUtilities.IsCoreClr9OrHigherRuntime` 分两支
    '''   （`IEnumerableWhereIterator` / `WhereEnumerableIterator`），本项目只 target `net10.0`（vbproj:6），故恒为前者。
    ''' </summary>
    <Fact>
    Public Sub DebuggerProxy_FrameworkTypes_IEnumerable_Exception()
        Dim obj As Object = Enumerable.Range(0, 10).Where(Function(i)
                                                              If i = 5 Then Throw New Exception("xxx")
                                                              Return i < 7
                                                          End Function)
        Assert.Equal("Enumerable.IEnumerableWhereIterator(Of Integer) { 0, 1, 2, 3, 4, !<Exception> ... }",
                     s_formatter.FormatObject(obj, SingleLineOptions))
    End Sub

    ''' <summary>
    ''' C# 基线 `DebuggerProxy_FrameworkTypes_IDictionary`（:518）与 `_Exception`（:537）。夹具：`ThrowingDictionary`。
    ''' 判别力：`ThrowingDictionary` 实现了 `IDictionary`，头部来自 `ICollection.Count`（`(10)`）而不是元素个数；
    '''   成员表是 `{ key, value }` 对（`FormatDictionaryMembers`）。
    '''   第一段（`throwAt:=-1`，不抛）钉住键值对渲染与 `SeparateLines` 的逐对成行；
    '''   第二段（`throwAt:=3`）钉住「枚举到第 3 项抛异常时保留前两项 + `!<Exception> ...`」。
    '''   撤销字典分支 ⇒ 会退化成成员表（`Count: 10, Keys: …`）；撤销异常降级 ⇒ 第二段异常外泄。
    ''' </summary>
    <Fact>
    Public Sub DebuggerProxy_FrameworkTypes_IDictionary()
        Dim obj As Object = New ThrowingDictionary(throwAt:=-1)
        Assert.Equal("ThrowingDictionary(10) { { 1, 1 }, { 2, 2 }, { 3, 3 }, { 4, 4 } }", s_formatter.FormatObject(obj, SingleLineOptions))

        AssertMembers(s_formatter.FormatObject(obj, SeparateLinesOptions), "ThrowingDictionary(10)",
            "{ 1, 1 }",
            "{ 2, 2 }",
            "{ 3, 3 }",
            "{ 4, 4 }")
    End Sub

    ''' <summary>C# 基线 `DebuggerProxy_FrameworkTypes_IDictionary_Exception`（ObjectFormatterTests.cs:537）。夹具：`ThrowingDictionary`。</summary>
    <Fact>
    Public Sub DebuggerProxy_FrameworkTypes_IDictionary_Exception()
        Dim obj As Object = New ThrowingDictionary(throwAt:=3)
        Assert.Equal("ThrowingDictionary(10) { { 1, 1 }, { 2, 2 }, !<Exception> ... }", s_formatter.FormatObject(obj, SingleLineOptions))
    End Sub

    ''' <summary>
    ''' C# 基线 `DebuggerProxy_FrameworkTypes_BitArray`（ObjectFormatterTests.cs:548）。夹具：无（BCL `BitArray`）。
    ''' 判别力：`BitArray` **没有**调试器代理性，走 `ICollection` 分支得到 `BitArray(32)` 头部 + 32 个 `True/False`。
    '''   撤销 `ICollection` 分支 ⇒ 会印成成员表（`Count: 32, IsReadOnly: False, …`）；把 32 个元素数错或截断 ⇒ 长度对不上。
    '''   这格是 `Boolean` 拼写与「集合元素不截断」的联合锚点。
    ''' </summary>
    <Fact>
    Public Sub DebuggerProxy_FrameworkTypes_BitArray()
        Dim obj = New BitArray(New Integer() {1})
        Assert.Equal("BitArray(32) { True, False, False, False, False, False, False, False, False, False, False, False, False, False, False, False, False, False, False, False, False, False, False, False, False, False, False, False, False, False, False, False }",
                     s_formatter.FormatObject(obj, SingleLineOptions))
    End Sub

    ''' <summary>
    ''' C# 基线 `DebuggerProxy_FrameworkTypes_Queue`（:557）/ `_Stack`（:569）。夹具：无（BCL）。
    ''' 判别力：两条分别钉住**枚举顺序**：`Queue` 是 FIFO（`1, 2, 3`）、`Stack` 是 LIFO（`3, 2, 1`）。
    '''   把任一类型的枚举换成另一种顺序 ⇒ 对应的那条失败。头部 `(3)` 来 `ICollection.Count`。
    ''' </summary>
    <Fact>
    Public Sub DebuggerProxy_FrameworkTypes_QueueAndStack()
        Dim queue As New Queue(Of Integer)()
        queue.Enqueue(1)
        queue.Enqueue(2)
        queue.Enqueue(3)
        Assert.Equal("Queue(Of Integer)(3) { 1, 2, 3 }", s_formatter.FormatObject(queue, SingleLineOptions))

        Dim stack As New Stack(Of Integer)()
        stack.Push(1)
        stack.Push(2)
        stack.Push(3)
        Assert.Equal("Stack(Of Integer)(3) { 3, 2, 1 }", s_formatter.FormatObject(stack, SingleLineOptions))
    End Sub

    ''' <summary>
    ''' C# 基线 `DebuggerProxy_FrameworkTypes_Dictionary`（:581）/ `_KeyValuePair`（:600）。夹具：无（BCL）。
    ''' 判别力：`Dictionary` 走字典分支（每个元素是 `{ key, value }` 对，键是字符串就带引号）；`KeyValuePair` **在根位置**时
    '''   头部是完整类型名（`KeyValuePair(Of Integer, String)`）而其成员直接摊平成 `{ 1, "x" }`——这是 `FormatObjectRecursive`
    '''   里 `IsRoot` 分支的结果。撤销 `KeyValuePair` 特判 ⇒ 变成 `KeyValuePair(Of Integer, String) { Key=1, Value="x" }`；
    '''   撤销 `IsRoot` 判断 ⇒ 根部会丢掉类型名。
    ''' </summary>
    <Fact>
    Public Sub DebuggerProxy_FrameworkTypes_DictionaryAndKeyValuePair()
        Dim dict As New Dictionary(Of String, Integer) From {{"x", 1}}

        AssertMembers(s_formatter.FormatObject(dict, SeparateLinesOptions), "Dictionary(Of String, Integer)(1)",
            "{ ""x"", 1 }")

        Assert.Equal("Dictionary(Of String, Integer)(1) { { ""x"", 1 } }", s_formatter.FormatObject(dict, SingleLineOptions))

        Assert.Equal("KeyValuePair(Of Integer, String) { 1, ""x"" }",
                     s_formatter.FormatObject(New KeyValuePair(Of Integer, String)(1, "x"), SingleLineOptions))
    End Sub

    ''' <summary>
    ''' C# 基线 `DebuggerProxy_FrameworkTypes_List`（:609）/ `_LinkedList`（:618）。夹具：无（BCL）。
    ''' 判别力：两个 `ICollection` 序列的头部都用 `(3)`，元素分别带 `'c'`（C#）与 `"c"c`（VB）字符字面量 / 纯整数。
    '''   撤销「序列元素按 primitive 渲染」⇒ 字符元素会退化成 `Char` 的成员表。
    ''' </summary>
    <Fact>
    Public Sub DebuggerProxy_FrameworkTypes_ListAndLinkedList()
        Dim list As New List(Of Object) From {1, 2, "c"c}
        Assert.Equal("List(Of Object)(3) { 1, 2, ""c""c }", s_formatter.FormatObject(list, SingleLineOptions))

        Dim linked As New LinkedList(Of Integer)()
        linked.AddLast(1)
        linked.AddLast(2)
        linked.AddLast(3)
        Assert.Equal("LinkedList(Of Integer)(3) { 1, 2, 3 }", s_formatter.FormatObject(linked, SingleLineOptions))
    End Sub

    ''' <summary>
    ''' C# 基线 `DebuggerProxy_FrameworkTypes_SortedList`（:630）与 `_SortedDictionary`（:648）。夹具：无（BCL）。
    ''' 判别力：排序容器的元素顺序是**按键排序**而非插入序（插入 3,1,2 后必须印 1,2,3）；`SortedList(Of Integer(), Integer())`
    '''   进一步钉住「键本身是数组时按键值渲染」（`Integer(1) { 3 }`）。`SortedDictionary` 那格用十六进制 PrintOptions，
    '''   钉住 `&H` 前缀与 8 位补零（VB 拼写，C# 基线是 `0x00000001`）。撤销基数传递 ⇒ 十进制；撤销补零 ⇒ `&H1`。
    ''' </summary>
    <Fact>
    Public Sub DebuggerProxy_FrameworkTypes_SortedListAndSortedDictionary()
        Dim sortedList As New SortedList(Of Integer, Integer)()
        sortedList.Add(3, 4)
        sortedList.Add(1, 5)
        sortedList.Add(2, 6)
        Assert.Equal("SortedList(Of Integer, Integer)(3) { { 1, 5 }, { 2, 6 }, { 3, 4 } }",
                     s_formatter.FormatObject(sortedList, SingleLineOptions))

        Dim arrays As New SortedList(Of Integer(), Integer())()
        arrays.Add(New Integer() {3}, New Integer() {4})
        Assert.Equal("SortedList(Of Integer(), Integer())(1) { { Integer(1) { 3 }, Integer(1) { 4 } } }",
                     s_formatter.FormatObject(arrays, SingleLineOptions))

        Dim sortedDict As New SortedDictionary(Of Integer, Integer)()
        sortedDict.Add(1, &H1A)
        sortedDict.Add(3, &H3C)
        sortedDict.Add(2, &H2B)
        Assert.Equal("SortedDictionary(Of Integer, Integer)(3) { { &H00000001, &H0000001A }, { &H00000002, &H0000002B }, { &H00000003, &H0000003C } }",
                     s_formatter.FormatObject(sortedDict, New PrintOptions With {.NumberRadix = ObjectFormatterHelpers.NumberRadixHexadecimal}))
    End Sub

    ''' <summary>
    ''' C# 基线 `DebuggerProxy_FrameworkTypes_HashSet`（:662）/ `_SortedSet`（:675）。夹具：无（BCL）。
    ''' 判别力：两者都不实现非泛型 `ICollection`（只实现 `ICollection(Of T)`），故头部走 `<DebuggerDisplay>` 的 `Count = 2`
    '''   而不是 `(2)`——`HashSet(Of Integer)(Count = 2)`。而 `SortedSet` **实现** `ICollection(Of T)` 且**实现**非泛型
    '''   `ICollection`，故头部是 `(2)`。两格相邻，正好钉住「`(n)` 只认非泛型 `ICollection`」这条判据：
    '''   把它放宽成 `ICollection(Of T)`，`HashSet` 那条就会从 `(Count = 2)` 变成 `(2)`。
    ''' </summary>
    <Fact>
    Public Sub DebuggerProxy_FrameworkTypes_HashSetAndSortedSet()
        Dim hashSet As New HashSet(Of Integer)()
        hashSet.Add(1)
        hashSet.Add(2)
        Assert.Equal("HashSet(Of Integer)(Count = 2) { 1, 2 }", s_formatter.FormatObject(hashSet, SingleLineOptions))

        Dim sortedSet As New SortedSet(Of Integer)()
        sortedSet.Add(1)
        sortedSet.Add(2)
        Assert.Equal("SortedSet(Of Integer)(2) { 1, 2 }", s_formatter.FormatObject(sortedSet, SingleLineOptions))
    End Sub

    ''' <summary>
    ''' C# 基线 `DebuggerProxy_FrameworkTypes_ConcurrentDictionary`（:686）/ `_ConcurrentQueue`（:697）/ `_ConcurrentStack`（:709）。
    ''' 夹具：无（BCL）。
    ''' 判别力：并发容器的头部与元素与各自的顺序语义一致（字典摊平成 `{ key, value }`、队列 FIFO、栈 LIFO）。
    '''   三格合并成一条是为减少同一判据的重复；合并后仍逐项独立断言，任一项顺序或形状写错都会失败。
    ''' </summary>
    <Fact>
    Public Sub DebuggerProxy_FrameworkTypes_ConcurrentCollections()
        Dim dict As New ConcurrentDictionary(Of String, Integer)()
        dict.AddOrUpdate("x", 1, Function(k, v) v)
        Assert.Equal("ConcurrentDictionary(Of String, Integer)(1) { { ""x"", 1 } }",
                     s_formatter.FormatObject(dict, SingleLineOptions))

        Dim queue As New ConcurrentQueue(Of Object)()
        queue.Enqueue(1)
        queue.Enqueue(2)
        queue.Enqueue(3)
        Assert.Equal("ConcurrentQueue(Of Object)(3) { 1, 2, 3 }", s_formatter.FormatObject(queue, SingleLineOptions))

        Dim stack As New ConcurrentStack(Of Object)()
        stack.Push(1)
        stack.Push(2)
        stack.Push(3)
        Assert.Equal("ConcurrentStack(Of Object)(3) { 3, 2, 1 }", s_formatter.FormatObject(stack, SingleLineOptions))
    End Sub

    ''' <summary>
    ''' C# 基线 `DebuggerProxy_FrameworkTypes_BlockingCollection`（:721）/ `_ReadOnlyCollection`（:743）。夹具：无（BCL）。
    ''' 判别力：`BlockingCollection` 的 `Add(2, CancellationToken)` 重载不改变渲染（仍 `(2) { 1, 2 }`）；
    '''   `ReadOnlyCollection(Of Integer)` 头部是 `(3)`（它实现了非泛型 `ICollection`）。撤销包装类型的 `ICollection`
    '''   识别 ⇒ 会退化成成员表。
    ''' </summary>
    <Fact>
    Public Sub DebuggerProxy_FrameworkTypes_BlockingCollectionAndReadOnlyCollection()
        Dim blocking As New BlockingCollection(Of Integer)()
        blocking.Add(1)
        blocking.Add(2, New CancellationToken())
        Assert.Equal("BlockingCollection(Of Integer)(2) { 1, 2 }", s_formatter.FormatObject(blocking, SingleLineOptions))

        Dim readOnly_ = New ReadOnlyCollection(Of Integer)(New Integer() {1, 2, 3})
        Assert.Equal("ReadOnlyCollection(Of Integer)(3) { 1, 2, 3 }", s_formatter.FormatObject(readOnly_, SingleLineOptions))
    End Sub

    ''' <summary>
    ''' C# 基线 `DebuggerProxy_FrameworkTypes_Lazy`（ObjectFormatterTests.cs:752，C# 侧标 `WindowsDesktopOnly`）。
    ''' 夹具：无（BCL `Lazy(Of T)`）。
    ''' 判别力：`Lazy(Of T)` 同时带 `<DebuggerDisplay>` 与 `<DebuggerTypeProxy>`，**两者渲染同一组信息**，故头部与成员表都要出现
    '''   `ThreadSafetyMode=…IsValueCreated=…IsValueFaulted=…Value=…` 四项；`Value` 创建前后各断言一次。
    '''   撤销 DD 或撤销代理任一侧 ⇒ 对应的那一侧（头部/成员表）会退化成类型名或成员表。
    '''   **与 C# 基线的差异（实锤，本条按 VB 实测写）**：C# 基线（仅桌面框架）在 `Value` 创建后仍印 `Mode=None`；
    '''   本机 CoreCLR 上印 `ThreadSafetyMode = Nothing` / `Mode=Nothing`——`Lazy(Of T)` 的 `Mode` 是**可空**枚举
    '''   （`LazyThreadSafetyMode?`），值一旦创建就被实现清空。（差异的成因是**推测**：C# 基线标 `WindowsDesktopOnly`
    '''   正是承认该断言在 CoreCLR 上不成立；两种读数均已实测。）
    ''' </summary>
    <Fact>
    Public Sub DebuggerProxy_FrameworkTypes_Lazy()
        Dim obj = New Lazy(Of Integer())(Function() New Integer() {1, 2}, LazyThreadSafetyMode.None)

        Assert.Equal("Lazy(Of Integer())(ThreadSafetyMode = None, IsValueCreated = False, IsValueFaulted = False, Value = Nothing) { IsValueCreated=False, IsValueFaulted=False, Mode=None, Value=Nothing }",
                     s_formatter.FormatObject(obj, SingleLineOptions))

        AssertMembers(s_formatter.FormatObject(obj, SeparateLinesOptions), "Lazy(Of Integer())(ThreadSafetyMode = None, IsValueCreated = False, IsValueFaulted = False, Value = Nothing)",
            "IsValueCreated: False",
            "IsValueFaulted: False",
            "Mode: None",
            "Value: Nothing")

        Assert.NotNull(obj.Value)

        Assert.Equal("Lazy(Of Integer())(ThreadSafetyMode = Nothing, IsValueCreated = True, IsValueFaulted = False, Value = Integer(2) { 1, 2 }) { IsValueCreated=True, IsValueFaulted=False, Mode=Nothing, Value=Integer(2) { 1, 2 } }",
                     s_formatter.FormatObject(obj, SingleLineOptions))

        AssertMembers(s_formatter.FormatObject(obj, SeparateLinesOptions), "Lazy(Of Integer())(ThreadSafetyMode = Nothing, IsValueCreated = True, IsValueFaulted = False, Value = Integer(2) { 1, 2 })",
            "IsValueCreated: True",
            "IsValueFaulted: False",
            "Mode: Nothing",
            "Value: Integer(2) { 1, 2 }")
    End Sub

    ''' <summary>
    ''' C# 基线 `DebuggerProxy_FrameworkTypes_Task`（ObjectFormatterTests.cs:788）。夹具：本文件内的 `MockDesktopTask` / `MockTaskProxy`
    ''' （逐字照搬 {{Roslyn}}\src\Scripting\CoreTestUtilities\ObjectFormatterFixtures\MockDesktopTask.cs 的形状，理由见文件头）。
    ''' 判别力：DD 的 `Method = {DebuggerDisplayMethodDescription}` 取的是**私有属性的返回值**（`m_action.Method.ToString()`），
    '''   期望值把它钉成 `"Void TaskMethod()"`（带引号的字符串字面量）——撤销「DD 嵌入表达式能取私有成员」⇒ 该段变成
    '''   `!<Member … not found>`；成员表来自代理（`AsyncState`/`CancellationPending`/`CreationOptions`/`Exception`/`Id`/`Status`），
    '''   撤销代理 ⇒ 成员表会变成 `m_action`。
    ''' </summary>
    <Fact>
    Public Sub DebuggerProxy_FrameworkTypes_Task()
        Dim obj = New MockDesktopTask(AddressOf TaskMethod)

        Assert.Equal("MockDesktopTask(Id = 1234, Status = Created, Method = ""Void TaskMethod()"") { AsyncState=Nothing, CancellationPending=False, CreationOptions=None, Exception=Nothing, Id=1234, Status=Created }",
                     s_formatter.FormatObject(obj, SingleLineOptions))

        AssertMembers(s_formatter.FormatObject(obj, SeparateLinesOptions), "MockDesktopTask(Id = 1234, Status = Created, Method = ""Void TaskMethod()"")",
            "AsyncState: Nothing",
            "CancellationPending: False",
            "CreationOptions: None",
            "Exception: Nothing",
            "Id: 1234",
            "Status: Created")
    End Sub

    Private Shared Sub TaskMethod()
    End Sub

    ''' <summary>
    ''' C# 基线 `DebuggerProxy_FrameworkTypes_SpinLock1`（:810）/ `SpinLock2`（:826）。夹具：本文件内的
    ''' `MockDesktopSpinLock`（逐字照搬 MockDesktopSpinLock.cs 的形状）。
    ''' 判别力：两格的分野是构造参数 `enableThreadOwnerTracking`：
    '''   关（第一格）时 `IsHeldByCurrentThread` **抛** `InvalidOperationException`，期望值是 `!<InvalidOperationException>`（异常降级）；
    '''   开（第二格）时同一成员返回 `True` 且 `OwnerThreadID` 为 `0`。
    '''   撤销异常降级 ⇒ 第一格异常外泄；把两格的开关搞反 ⇒ 两条都失败。
    '''   `IsHeld=false` 与 DD 的头部 `IsHeld = False` 在两个方向上都相同，是夹具自身的常量（C# 基线同样如此）。
    ''' </summary>
    <Fact>
    Public Sub DebuggerProxy_FrameworkTypes_SpinLock()
        Dim tracked As New MockDesktopSpinLock(enableThreadOwnerTracking:=False)

        Assert.Equal("MockDesktopSpinLock(IsHeld = False) { IsHeld=False, IsHeldByCurrentThread=!<InvalidOperationException>, OwnerThreadID=Nothing }",
                     s_formatter.FormatObject(tracked, SingleLineOptions))

        AssertMembers(s_formatter.FormatObject(tracked, SeparateLinesOptions), "MockDesktopSpinLock(IsHeld = False)",
            "IsHeld: False",
            "IsHeldByCurrentThread: !<InvalidOperationException>",
            "OwnerThreadID: Nothing")

        Dim untracked As New MockDesktopSpinLock(enableThreadOwnerTracking:=True)

        Assert.Equal("MockDesktopSpinLock(IsHeld = False) { IsHeld=False, IsHeldByCurrentThread=True, OwnerThreadID=0 }",
                     s_formatter.FormatObject(untracked, SingleLineOptions))

        AssertMembers(s_formatter.FormatObject(untracked, SeparateLinesOptions), "MockDesktopSpinLock(IsHeld = False)",
            "IsHeld: False",
            "IsHeldByCurrentThread: True",
            "OwnerThreadID: 0")
    End Sub

    ''' <summary>
    ''' C# 基线 `DebuggerProxy_ArrayBuilder`（ObjectFormatterTests.cs:862）。夹具：无（`Microsoft.CodeAnalysis.PooledObjects.ArrayBuilder(Of T)`，经 IVT）。
    ''' 判别力：`ArrayBuilder` 的 `<DebuggerDisplay("Count = {Count}")>` 给出头部 `(Count = 5)`，代理给出 5 个元素。
    '''   撤销 DD ⇒ 退化成 `ArrayBuilder(Of Integer)`；撤销代理 ⇒ 成员表会变成池化实现字段（`_builder`/`_pool` 之类），
    '''   这类字段名一旦泄漏到输出就说明代理没生效。
    ''' </summary>
    <Fact>
    Public Sub DebuggerProxy_ArrayBuilder()
        Dim obj = ArrayBuilder(Of Integer).GetInstance()
        obj.AddRange(New Integer() {1, 2, 3, 4, 5})

        Assert.Equal("ArrayBuilder(Of Integer)(Count = 5) { 1, 2, 3, 4, 5 }", s_formatter.FormatObject(obj, SingleLineOptions))

        AssertMembers(s_formatter.FormatObject(obj, SeparateLinesOptions), "ArrayBuilder(Of Integer)(Count = 5)",
            "1",
            "2",
            "3",
            "4",
            "5")

        obj.Free()
    End Sub

#End Region

#Region "异常栈渲染（StackTrace_*，design-detailed.md §U8 的 7 格）"

    ' 7 格轴（§U8 原文）：泛型方法 / 泛型类型 / 泛型类型内泛型方法 / dynamic / ref / out / 泛型 ByRef。
    ' VB 没有 `dynamic` 关键字，C# 的 `dynamic` 派发在 VB 的对应物是**晚期绑定**（`Microsoft.VisualBasic.CompilerServices.NewLateBinding`），
    ' 故 `StackTrace_Dynamic` 断言的是晚期绑定帧。C# 把 ref 与 out 合成一格（`RefOutParameters`），VB 分成两格，
    ' 因为 `VisualBasicObjectFormatterImpl.vb:24-26` 的 `FormatRefKind` 对二者印**不同**前缀（`ByRef` vs `<Out> ByRef`）。
    ' 加上 C# 基线的 `StackTrace_NonGeneric` 对照，本区共 8 条。

    ''' <summary>
    ''' C# 基线 `StackTrace_NonGeneric`（ObjectFormatterTests.cs:931，C# 侧 `[Fact(Skip=…)]`，本格**不跳过**）。
    ''' 夹具：`StackFixture`（`#ExternalSource` 区域，见 `StackDocPath`）。
    ''' 判别力：期望值把三件事同时钉住——
    '''   (1) 头部是 `类型全名: 消息`（`builder.Append(e.GetType())` + `": "` + `e.Message`，CommonObjectFormatter.cs:72-75）；
    '''       消息用 `New Exception().Message` 在运行时取，避免绑定运行时的本地化文本；
    '''   (2) 帧行前缀恰为两空格 + `+` + 空格，且方法签名是「`<类型全名>.<方法名>()`」；
    '''   (3) 文件/行号后缀来自 `ScriptingResources.AtFileLine` 的 ` at {0} : {1}`（本地化），行号由 `#ExternalSource` 钉死。
    '''   撤销 (2) 的格式 ⇒ 前缀或分隔符对不上；撤销 (3) 的 PDB 映射 ⇒ 后缀整段消失。
    '''   C# 基线的同一条用例把**帧行**写死为 `+ …` + 资源后缀，本格结构同形；不同的是 C# 的 `#line` 让测试方法帧也带 ` at …`，
    '''   而 VB 的 `#ExternalSource` 只覆盖夹具区域（实锤），故第二帧无后缀。
    ''' **C# 基线那 7 格的期望值是错的（依据）**：它们把首行写成 `{new Exception().Message}`，**漏了 `e.GetType()` 的前缀**——
    '''   产品码 `CommonObjectFormatter.cs:72-75` 逐字是 `builder.Append(e.GetType()); builder.Append(": ");`+`builder.Append(e.Message);`，
    '''   故首行必然是 `异常类型全名: 消息`（本格实测 `System.Exception: …`）。那 7 格
    '''   （`ObjectFormatterTests.cs:931/954/978/1002/1034/1074/1099`）**全部**挂着 `[Fact(Skip=…)]`（issue 19027 / 9221）**从未运行**，
    '''   所以这个错一直没被发现——这也正是「消息头含类型名」这条主张的旁证（读码 + 从未跑过两条证据）。本格按产品码 + 实测写，不照抄 C#。
    ''' </summary>
    <Fact>
    Public Sub StackTrace_NonGeneric()
        Try
            StackFixture.Method()
        Catch e As Exception
            Assert.Equal(GetType(Exception).FullName & ": " & New Exception().Message & NL() &
                         "  + " & GetType(StackFixture).FullName & ".Method()" & AtFileLine(10004) & NL() &
                         "  + " & GetType(ScriptModeObjectFormatterTests).FullName & ".StackTrace_NonGeneric()" & NL(),
                         s_formatter.FormatException(e))
        End Try
    End Sub

    ''' <summary>
    ''' C# 基线 `StackTrace_GenericMethod`（ObjectFormatterTests.cs:954；C# 侧 Skip）。夹具：`StackFixture.Method(Of U)`。
    ''' 判别力：期望值印的是**类型参数名** `Method(Of U)` 而不是实例化后的 `Method(Of Char)`——这正是 C# 基线注释
    '''   `// TODO (DevDiv #173210): Should show Fixture.Method<char>` 记录的行为（VB 实测同形，只是拼写为 VB 的 `(Of U)`）。
    '''   把签名渲染改成用实例化实参 ⇒ 这一格失败（并说明 TODO 被实现了，需同步 C# 基线）。
    ''' </summary>
    <Fact>
    Public Sub StackTrace_GenericMethod()
        Try
            StackFixture.Method(Of Char)()
        Catch e As Exception
            Assert.Equal(GetType(Exception).FullName & ": " & New Exception().Message & NL() &
                         "  + " & GetType(StackFixture).FullName & ".Method(Of U)()" & AtFileLine(10009) & NL() &
                         "  + " & GetType(ScriptModeObjectFormatterTests).FullName & ".StackTrace_GenericMethod()" & NL(),
                         s_formatter.FormatException(e))
        End Try
    End Sub

    ''' <summary>
    ''' C# 基线 `StackTrace_GenericType`（ObjectFormatterTests.cs:978；C# 侧 Skip）。夹具：`StackFixture(Of T).Method()`。
    ''' 判别力：泛型类型的声明类型名必须印成 `StackFixture(Of T)`（`FormatTypeName` 的泛型分支），
    '''   撤销该分支 ⇒ 退化成 `StackFixture` 或 CLR 的 ``StackFixture`1``；把类型参数名写成实参 `Integer` ⇒ 也失败。
    ''' </summary>
    <Fact>
    Public Sub StackTrace_GenericType()
        Try
            StackFixture(Of Integer).Method()
        Catch e As Exception
            Assert.Equal(GetType(Exception).FullName & ": " & New Exception().Message & NL() &
                         "  + " & StackFixtureGenericName & ".Method()" & AtFileLine(10016) & NL() &
                         "  + " & GetType(ScriptModeObjectFormatterTests).FullName & ".StackTrace_GenericType()" & NL(),
                         s_formatter.FormatException(e))
        End Try
    End Sub

    ''' <summary>
    ''' C# 基线 `StackTrace_GenericMethodInGenericType`（ObjectFormatterTests.cs:1002；C# 侧 Skip）。
    ''' 夹具：`StackFixture(Of T).Method(Of U)`。
    ''' 判别力：这一格是前两格的**合取**：两种泛型参数必须**同时**出现且顺序固定（类型参数在前、方法参数在后）。
    '''   撤销任一侧的泛型渲染只会让这一格失败而前两格仍过——这正是它作为独立格的价值。
    ''' </summary>
    <Fact>
    Public Sub StackTrace_GenericMethodInGenericType()
        Try
            StackFixture(Of Integer).Method(Of Char)()
        Catch e As Exception
            Assert.Equal(GetType(Exception).FullName & ": " & New Exception().Message & NL() &
                         "  + " & StackFixtureGenericName & ".Method(Of U)()" & AtFileLine(10021) & NL() &
                         "  + " & GetType(ScriptModeObjectFormatterTests).FullName & ".StackTrace_GenericMethodInGenericType()" & NL(),
                         s_formatter.FormatException(e))
        End Try
    End Sub

    ''' <summary>
    ''' C# 基线 `StackTrace_Dynamic`（ObjectFormatterTests.cs:1034；C# 侧 Skip）。夹具：`DynamicStackFixture`。
    ''' VB 对应物 = 晚期绑定（VB 无 `dynamic`）。
    ''' 判别力：三条断言各钉一层——
    '''   (1) 头部类型是 `System.MissingMemberException`、消息来自运行时的 VB 资源（`Public member 'x' on type 'Object' not found.`），
    '''       消息在运行时从捕获到的异常取，故不绑定语言；
    '''   (2) **VB 运行时帧留在输出里**：`Symbols.Container.GetMembers` 与 `NewLateBinding.CallMethod`。二者**没有**
    '''       `DebuggerHiddenAttribute`（实测：`attrs=[RequiresUnreferencedCodeAttribute]`），故 `CommonMemberFilter.Include` 不剪它们；
    '''   (3) 相邻的 `NewLateBinding.ObjectLateCall` / `LateCall` **被剪掉**（实测 `attrs=[DebuggerHiddenAttribute,DebuggerStepThroughAttribute,…]`），
    '''       它们不出现在输出里，但又确实在原始 `StackTrace` 帧序列中——这一格因此同时验证「剪」与「不剪」两侧。
    '''   撤销 (3) 用的 `DebuggerHidden` 判据 ⇒ 输出会多两行；撤销 (2) 的帧渲染 ⇒ 输出少行。
    '''   帧签名含 VB 运行时的完整形参表，故本格对 `Microsoft.VisualBasic.Core` 的方法签名敏感（升级运行时时须重测）。
    ''' </summary>
    <Fact>
    Public Sub StackTrace_Dynamic()
        Try
            DynamicStackFixture.MethodDynamic()
            Assert.True(False, "the late bound call was expected to throw")
        Catch e As Exception
            Assert.Equal("System.MissingMemberException: " & e.Message & NL() &
                         "  + Microsoft.VisualBasic.CompilerServices.Symbols.Container.GetMembers(ByRef String, Boolean)" & NL() &
                         "  + Microsoft.VisualBasic.CompilerServices.NewLateBinding.CallMethod(Microsoft.VisualBasic.CompilerServices.Symbols.Container, String, Object(), String(), System.Type(), Boolean(), System.Reflection.BindingFlags, Boolean, ByRef Microsoft.VisualBasic.CompilerServices.OverloadResolution.ResolutionFailure)" & NL() &
                         "  + " & GetType(DynamicStackFixture).FullName & ".MethodDynamic()" & AtFileLine(20007) & NL() &
                         "  + " & GetType(ScriptModeObjectFormatterTests).FullName & ".StackTrace_Dynamic()" & NL(),
                         s_formatter.FormatException(e))
        End Try
    End Sub

    ''' <summary>
    ''' C# 基线 `StackTrace_RefOutParameters`（ObjectFormatterTests.cs:1074；C# 侧 Skip）的 **ref 一半**。
    ''' 夹具：`ParameterStackFixture.Method(ByRef c As Char, ByRef d As DateTime)`。
    ''' 判别力：`FormatRefKind`（`VisualBasicObjectFormatterImpl.vb:24-26`）对「只是 `ByRef`、没有 `<Out>`」的形参必须印 `ByRef`
    '''   而**不是** `<Out> ByRef`；参数类型按 VB 拼写印（`Char`、`Date`——`System.DateTime` 在 VB 侧是 `Date`，
    '''   这是与 C# 基线 `System.DateTime` 的真实差异）。把 `IsOut` 判据写成 `ParameterType.IsByRef` ⇒ 两个参数都会带上 `<Out>`。
    ''' </summary>
    <Fact>
    Public Sub StackTrace_ByRefParameters()
        Try
            Dim c As Char = " "c
            Dim d As DateTime
            ParameterStackFixture.Method(c, d)
        Catch e As Exception
            Assert.Equal(GetType(Exception).FullName & ": " & New Exception().Message & NL() &
                         "  + " & GetType(ParameterStackFixture).FullName & ".Method(ByRef Char, ByRef Date)" & AtFileLine(10028) & NL() &
                         "  + " & GetType(ScriptModeObjectFormatterTests).FullName & ".StackTrace_ByRefParameters()" & NL(),
                         s_formatter.FormatException(e))
        End Try
    End Sub

    ''' <summary>
    ''' **out 一半**（C# 基线把 ref/out 合成 `StackTrace_RefOutParameters`，VB 分成两格，理由见本节抬头）。
    ''' 夹具：`ParameterStackFixture.MethodOut(ByRef c As Char, &lt;Out&gt; ByRef d As DateTime)`。
    ''' 判别力：只有带 `<Out>` 的形参印 `<Out> ByRef`，另一个仍是 `ByRef`——同一行里两种前缀**同时**出现，
    '''   是本区里唯一能同时否定「全印 ByRef」与「全印 &lt;Out&gt; ByRef」两种错误实现的一格。
    ''' </summary>
    <Fact>
    Public Sub StackTrace_OutParameters()
        Try
            Dim c As Char = " "c
            Dim d As DateTime
            ParameterStackFixture.MethodOut(c, d)
        Catch e As Exception
            Assert.Equal(GetType(Exception).FullName & ": " & New Exception().Message & NL() &
                         "  + " & GetType(ParameterStackFixture).FullName & ".MethodOut(ByRef Char, <Out> ByRef Date)" & AtFileLine(10033) & NL() &
                         "  + " & GetType(ScriptModeObjectFormatterTests).FullName & ".StackTrace_OutParameters()" & NL(),
                         s_formatter.FormatException(e))
        End Try
    End Sub

    ''' <summary>
    ''' C# 基线 `StackTrace_GenericRefParameter`（ObjectFormatterTests.cs:1099；C# 侧 Skip）的 VB 对应格。
    ''' 夹具：`ParameterStackFixture.Method(Of U)(ByRef value As U)`。
    ''' 判别力：泛型 `ByRef` 形参要同时过两条分支——先印 `ByRef`（`parameter.ParameterType.IsByRef`）再印**元素类型**的类型参数名
    '''   （`parameter.ParameterType.GetElementType()`），得到 `ByRef U`。撤销 `GetElementType()` ⇒ 印成 `ByRef U&`
    '''   （`ByRef` 的反射类型名带 `&`）；撤销 `FormatRefKind` ⇒ 丢掉 `ByRef`。C# 基线同格注释记录它印的是 `ref U` 而非实例化的 `ref char`。
    ''' </summary>
    <Fact>
    Public Sub StackTrace_GenericByRefParameter()
        Try
            Dim c As Char = " "c
            ParameterStackFixture.Method(Of Char)(c)
        Catch e As Exception
            Assert.Equal(GetType(Exception).FullName & ": " & New Exception().Message & NL() &
                         "  + " & GetType(ParameterStackFixture).FullName & ".Method(Of U)(ByRef U)" & AtFileLine(10038) & NL() &
                         "  + " & GetType(ScriptModeObjectFormatterTests).FullName & ".StackTrace_GenericByRefParameter()" & NL(),
                         s_formatter.FormatException(e))
        End Try
    End Sub

    ''' <summary>
    ''' 参数为 `Nothing` 时的边界（不在 C# 基线里，VB 侧补）：`FormatException` 必须抛 `ArgumentNullException` 且形参名是 `e`
    '''   （`CommonObjectFormatter.cs:64-67`）。
    ''' 判别力：撤销那处 null 检查 ⇒ 后续 `e.GetType()` 抛 `NullReferenceException`，断言类型就变了。
    ''' </summary>
    <Fact>
    Public Sub StackTrace_NullException()
        Dim ex = Assert.Throws(Of ArgumentNullException)(Function() s_formatter.FormatException(Nothing))
        Assert.Equal("e", ex.ParamName)
    End Sub

#End Region

#Region "测试内夹具"

    ' MockDesktopTask / MockTaskProxy / MockDesktopSpinLock / CoreRangeIterator 都在
    ' ScriptModeObjectFormatterFixtures.vb（顶层类型）——放在嵌套类里会给输出带上测试类名前缀。

#End Region

End Class

' ==================================================================================================================
' 栈帧夹具（`StackTrace_*` 用）。**必须放在本文件内**：实测（`U8ProbeStackTemporary` 探针，两次读数）
'   ——当某文件含 `#ExternalSource` 时，该文件里**区域外**的方法帧也没有真实文件信息（`GetFileName()` 为 Nothing）；
'   把夹具搬到另一个文件后，本文件的测试方法帧反而带上了本机的绝对路径，期望值不可移植。故区域与断言同文件。
' 晚期绑定的那一格夹具（`DynamicStackFixture`）需要 `Option Strict Off`，而该指令是文件级的，故它单独在
' `ScriptModeObjectFormatterFixtures.vb` 里；那个文件同样含 `#ExternalSource`，两边的帧都可映射。
'
' **维护纪律（与 C# 基线 ObjectFormatterTests.cs:890-896 的告诫同源）**：往本区域**末尾**加成员不会改已钉住的行号；
' 在中间插入行会改，必须同步 `StackTrace_*` 各格的 `AtFileLine(N)`。
' 行号从指令的下一行起算（实测）：`#ExternalSource(…, 10000)` 的下一物理行即 10000；帧报的是 **`Throw` 语句所在行**。
' ==================================================================================================================
#ExternalSource("z:\Fixture.vb", 10000)

Friend NotInheritable Class StackFixture
    <MethodImpl(MethodImplOptions.NoInlining)>
    Public Shared Sub Method()
        Throw New Exception()
    End Sub

    <MethodImpl(MethodImplOptions.NoInlining)>
    Public Shared Sub Method(Of U)()
        Throw New Exception()
    End Sub
End Class

Friend NotInheritable Class StackFixture(Of T)
    <MethodImpl(MethodImplOptions.NoInlining)>
    Public Shared Sub Method()
        Throw New Exception()
    End Sub

    <MethodImpl(MethodImplOptions.NoInlining)>
    Public Shared Sub Method(Of U)()
        Throw New Exception()
    End Sub
End Class

Friend NotInheritable Class ParameterStackFixture
    <MethodImpl(MethodImplOptions.NoInlining)>
    Public Shared Sub Method(ByRef c As Char, ByRef d As DateTime)
        Throw New Exception()
    End Sub

    <MethodImpl(MethodImplOptions.NoInlining)>
    Public Shared Sub MethodOut(ByRef c As Char, <System.Runtime.InteropServices.Out> ByRef d As DateTime)
        Throw New Exception()
    End Sub

    <MethodImpl(MethodImplOptions.NoInlining)>
    Public Shared Sub Method(Of U)(ByRef value As U)
        Throw New Exception()
    End Sub
End Class

#End ExternalSource
