# [BUG→撤销] 脚本类的实例成员体内显式 `Me` / `MyClass` / `MyBase` 被拒：BC36966 的判据是容纳类型，不是「是否顶层代码」

> **⚠ 撤销改判（作者裁定，2026-09-23）：本条不判为缺陷，修复须回退。** 取证 C# 的实际策略（`decisions.md` **D7 冲突裁定**第二个实例）：`Compilers\CSharp\Portable\Binder\Binder_Expressions.cs:55-73` 的 `HasThis` 逐字 `return !inTopLevelScriptMember || !isExplicit;` ⇒ **C# 在脚本类内任何位置都拒绝显式 `this` / `base`，成员体也算**（CS0027 / CS1512；作者另用 `Microsoft (R) Visual C# 交互窗口编译器 5.10.0-1.26380.3` 实跑 `void test() { this.ToString(); }` → `(1,15): error CS0027` 复核。本仓 `SemanticErrorTests.cs:1365` 的 `this.goo(); // 5` 只作旁证——同段 `:1381` 的 `// OK` 在注释块内，未逐行核对前不作锚点），也就是说**本条原判据（按容纳类型划界）就是 C# 同形**。作者据此裁定**「照 C# 回退」**，并明示接受被放弃的收益（成员体内无法限定访问被局部遮蔽的顶层字段，只能改局部名）。
> **回退是「部分回退」**：只回退"实例成员体放行显式关键字"这一条（`tasks\script-class-explicit-keyword-parity-revert\`，判据 K1–K12）。**保留**不回退的两项——① `Shared` 成员里的显式引用落普通共享诊断 BC30043（隐式落 BC30369），因为 C# 的静态检查（`:45-49` ⇒ CS0026/CS1511）**排在脚本门之前**，这条次序本身就是 C# 同形；② `MyBase` → `System.Object` 兜底仍按 2026-09-22 的第一次改判删除。
> 已落地的 F01/F02/F03（档 1 全绿、未提交）因此**不是要提交的功能**，而是本回退的输入；本文件其余内容按登记时原判读保留作证据，`spec\spec-scripting-dialect.md:234`/`:246-274`/`:299`/`:310`/`:312`/`:334`/`:339`/`:385` 与 `zh-CN` 已按新裁定改写。

**状态**：**Not A Bug（撤销改判，2026-09-23；原判「过宽属缺陷」被 C# 实测推翻）**——实施线转为 `tasks\script-class-explicit-keyword-parity-revert\`（部分回退），前任务 `tasks\script-class-explicit-me-scope\` 的 F01/F02/F03 已档 1 通过但**不再作为功能提交**
**证据等级**：**已运行**（Release 发布版宿主 `2.0.0-Beta+c15a959`，本机实测；另有普通类同形状对照 4 路（C / M / G1 / K），均**已运行**）。16 个探针的源码与逐条输出固化在 `tmp\spec-check-me\run-results-2026-09-22.txt`
**严重度**：中（不崩、有诊断，但诊断文案指向错误位置，且剥夺了 VB 里绕过局部遮蔽的**唯一**写法）
**影响面**：任何脚本编译（`.vbx` 文件执行、vbi REPL 提交）里**顶层 `Sub` / `Function` / `Property` 的成员体**，以及其中书写的 lambda；`vbc` 不产生脚本类编译，不受影响

## 症状

顶层 `Function` 体内写一个显式 `Me`（探针 `tmp\spec-check-me\B-toplevel-func-me.vbx`，摘录与文件逐行一致）：

```vb
Imports System

Dim counter As Integer = 0

Function NextCounter() As Integer
    Dim counter As Integer = 5
    Return counter
End Function

Function ReadCounter() As Integer
    Return Me.counter
End Function
```

```text
B-toplevel-func-me.vbx(11) : error BC36966: 您不能使用顶级脚本代码中的“Me”
```

三点都不对劲：

1. **这段代码不是顶层脚本代码**——它写在用户自己声明的 `Function` 体内，而消息逐字说「顶级脚本代码」（英文原文 `You cannot use '{0}' in top-level script code`，`VBResources.resx:4418-4420`）。
2. 该 `Function` 按本方言自己的定义**是脚本类的实例成员**（`spec\spec-scripting-dialect.md:16`、`:60`），`Me` 在其中语义完备。
3. `Me` 是 VB 里**穿过局部遮蔽拿到字段**的唯一写法（VB 无 `Me` 之外的容器限定手段，见下文「遮蔽无处可逃」），所以禁令的实际后果是字段在该方法内**不可达**，只能改局部变量名。

同一顶层容器里的其余形状（**全部已运行**，均报 BC36966 且消息同样说「顶级脚本代码」）：

| 探针 | 形状 | 实测 |
|---|---|---|
| `A-toplevel-stmt-me.vbx` | **顶层语句**里 `Me.counter` | BC36966 —— 这一格是**应当**禁的 |
| `D2-toplevel-prop-me.vbx` | 顶层 `ReadOnly Property` 的 getter 体内 | BC36966 |
| `E-myclass-mybase.vbx:24` | 顶层 **`Shared`** `Function` 体内 `MyClass` | BC36966（真实原因被顶掉，见对照表） |
| `E-myclass-mybase.vbx:28` | 顶层实例 `Function` 体内 `MyClass` | BC36966 |
| `I-lambda-in-toplevel-sub.vbx` | 顶层 `Sub` 体内书写的 **lambda** 里 `Me.counter` | BC36966 |
| `J-lambda-in-toplevel-stmt.vbx` | **顶层语句**里书写的 lambda 内 `Me.counter` | BC36966（与 A 同判据；这一格修复后**仍应**禁） |
| `L-toplevel-field-init-me.vbx` | 顶层 `Dim x As Integer = Me.counter`（字段初始化器） | BC36966（修复后仍应禁，但见「判据陷阱」） |

## 对照（已运行）

**同一段方法体，两种命运**——普通类里合法，脚本类顶层被拒：

| 形状 | 普通类里（脚本内声明的 `Class`） | 脚本类顶层成员里 |
|---|---|---|
| 实例方法体内 `Me.字段`（局部遮蔽该字段） | ✅ 合法，读到**字段** `0`（`C-class-func-me.vbx`、`M-ordinary-class-escape.vbx`） | ❌ BC36966 |
| 实例方法体内 `MyClass.字段` | ✅ 合法，读到字段 `0`（`M-ordinary-class-escape.vbx`） | ❌ BC36966 |
| `Shared` 方法体内显式 `Me` | ❌ **BC30043**「"'{0}' is valid only within an instance method."」（`G1-class-shared-me.vbx`）—— 说的是真原因 | ❌ BC36966「不能使用**顶级脚本代码**中的 Me」—— 位置与原因都不对 |
| 实例方法体内 `MyBase.成员`（无 `Inherits`，基类型为 `Object`） | ✅ 合法，输出 `Submission#0+NoBase`（`K-mybase-no-base-class.vbx`） | 现状 BC36966；**判据解禁后取不到基类成员是预期**（脚本类无基类型，与 C# 同形）⇒ 归 issue 29 改判后的回退与收严项 |

**遮蔽无处可逃**（实锤）：脚本类的合成容器名**不可书写**——`Script.counter` → BC30451「未声明"Script"」，`Global.Script.counter` → BC30456「"'Script'"不是"Global"的成员」，而容器实际叫 `Submission#0`（`MethodBase.GetCurrentMethod().DeclaringType` 实测），`#` 是类型字符、源码里写不出来。顶层 `Function` 里被局部遮蔽的字段因此**完全不可达**（`H-shadowed-no-escape.vbx` 输出 `unqualified read = 5`，字段的 `42` 拿不到）。

## 根因（源码核实）

`Compilers\VisualBasic\Portable\Binding\Binder_Expressions.vb:2257-2289`（`CheckMeOrMyBaseOrMyClassInSharedOrDisallowedContext`）：

```vb
Dim containingType = Me.ContainingType
If containingType IsNot Nothing AndAlso containingType.IsScriptClass Then   ' :2263-2264 ← 只问容纳类型
    If Not implicitReference Then
        errorId = ERRID.ERR_KeywordNotAllowedInScript                       ' :2265-2267 ← 显式 ⇒ 一律拒
        Return False
    End If
    If Not IsMeOrMyBaseOrMyClassInSharedContext() Then
        Return True                                                          ' :2270-2272（issue 08 的修法）
    End If
End If

If IsMeOrMyBaseOrMyClassInSharedContext() Then
    errorId = If(implicitReference, ERRID.ERR_BadInstanceMemberAccess,       ' :2275-2281 BC30369 / BC30043
                 If(…, ERRID.ERR_UseOfKeywordFromModule1, ERRID.ERR_UseOfKeywordNotInInstanceMethod1))
```

- 判据是 **容纳类型是脚本类**，与「该语句是否顶层代码」无关 ⇒ 覆盖脚本类的**每一个**成员体。`ERR_KeywordNotAllowedInScript = 36966`（`Errors.vb:1599`）。
- **同一函数里的 `Return` 半条用的是正确判据**：`Return` 只在「顶层脚本代码且不在脚本初始化器之内」被拒（`Binder_Statements.vb:5114`），顶层 `Sub`/`Function` 体内的 `Return` 是该方法的普通返回（`spec\spec-scripting-dialect.md:272` 自己写明两条限制「条件并不相同」）。**同一条错误码、同一个 "top-level script code" 消息，一半按顶层判、一半按容纳类型判** ⇒ 过宽是缺陷而非设计意图的正面书面证据。
- **变更史**：该判据由基线导入 commit `e814cb1`（"add base compiler"，2026-07-30）逐字带入上游 Roslyn 的形状；本 fork 在 `48d8edb`（"crash fixes"）改掉的是**隐式**侧的行为（issue 08 把「脚本类里没有共享代码」的错误前提修掉），**显式**侧只是把 `Else` 重写成 `If Not implicitReference Then` 的守卫形状（`:2265`）、报错行为逐字保留——那条保留决策即本 issue 要推翻的对象。

## 预期行为（判据）

1. **顶层代码里禁**：顶层语句、脚本类的字段/属性初始化器，以及在二者之中书写的 lambda 与查询表达式——显式 `Me` / `MyBase` / `MyClass` 报 **BC36966**，消息文案（"top-level script code"）在此处即为准确。
2. **顶层成员体内放行**：顶层 `Sub` / `Function` / `Property` 是脚本类的普通成员，其体内三个关键字走**普通类规则**（`Me.field` 能穿过局部遮蔽拿到字段；`MyClass` 合法；`MyBase` 按 `System.Object` 解析，见下节）。其中书写的 lambda 同属该成员体。
3. **`Shared` 成员不享受豁免**：显式引用落普通类判据 **BC30043**（`ERR_UseOfKeywordNotInInstanceMethod1`），隐式引用落 **BC30369**（`ERR_BadInstanceMemberAccess`）——与对照表第三行的普通类实测一致；BC36966 不得顶掉这两条。
4. 方言与 C# 的对照因此收窄为一句：C# 的 `this` 在 csx 顶层代码里也可用，VB 在顶层代码里拒绝显式关键字；成员体内两侧同形。

规范侧的落点见 `spec\spec-scripting-dialect.md:234`、`:246-270`（`zh-CN\` 译文同步行号）。

## 被本 issue 推翻的既有决策

> **⚠ 方向反转注记（2026-09-23）**：本 issue 已撤销改判（见顶部横幅），因此**下表第 2–7 行的原口径重新生效**——它们当初被「作废」的理由（判据过宽是缺陷）不再成立。两处例外需要单独读：① 第 1 行（`spec`）现按「禁令覆盖整个脚本类 + 共享判定先判」的 C# 同形措辞改写，不再声称是「与 C# 的分叉」，唯一保留的对上游分叉＝**判定次序**，登记在 `..\upstream-merge.md` §2.25；② 第 8 行的支撑事实已由会议记录就地订正（树内确有上游断言该禁令的用例）。下方「修复方向（候选，未拍板）」与「修复后会新增的行为变化」**同样只作历史证据**，其"修复后"列已被回退抵消（实测红名单见 `tmp\vortex-logs\script-class-explicit-keyword-parity-revert\01-main-rv-f01.md`）。

| 锚点 | 原文口径 | 与本判据的关系 |
|---|---|---|
| `spec\spec-scripting-dialect.md:234`、`:246`、`:250-268`、`:270`、`:299`、`:306`、`:308`、`:334`、`:339`、`:381`（`zh-CN\` 译文逐行同位） | 「条件是容纳类型是脚本类，覆盖顶层 `Sub`/`Function` 的整个方法体」+「有意的限制而非含义的改变」+「`Me` 限制是 Visual Basic 自己的（与 C# 的分叉之一）」 | 已被本判据取代：规范现按上下文划定禁令，并把该限制列为与 C# 的分叉之外的唯一差异 |
| `tasks\script-top-level-crashes\design-detailed.md:178` ②、「判据形状」第 1 条（`:182`）、「不变量」第 2 条（`:189`） | 「**显式**引用路径**逐字保持**」「脚本类 + 显式 `Me`/`MyClass` → BC36966（不变）」「显式 `Me` 仍 BC36966（诊断族不变）」 | **作废**：该选择在「只修崩溃」的单元范围内成立，作为长期口径被本 issue 判为缺陷（三处均已就地标注） |
| `tasks\script-top-level-crashes\design-overview.md:74`（F08）、`README.md:227`（F08 验收行） | 「只对**显式**引用保留 `ERR_KeywordNotAllowedInScript`（BC36966）」 | 同上，**作废**（已就地标注） |
| `tasks\script-top-level-crashes\test-plan.md:88`（F08-L2-5） | 「显式 `Me`（回归）→ BC36966（**不变**）」 | **作废**：用例期望值随本判据改变（顶层代码格不变，成员体格转合法，`Shared` 格换码） |
| `tasks\script-top-level-crashes-2\test-plan.md:22`（L2） | 「顶层裸 `MyBase`；顶层 `Sub` 内；顶层 `Shared Sub` 内——三者都报 `BC36966`」 | **部分作废**：第一格保持，后两格随本判据改变（已就地标注） |
| `tasks\script-mode-coverage-parity\README.md:278`（F5） | 「脚本类的显式 `Me`/`MyBase`/`MyClass` 报 `BC36966`」，依据指向规范的诊断族表 | **范围过期**：该行「报出诊断即覆盖」的判定仍成立，但报点范围以本 issue 为准（已就地标注） |
| `meetings\meeting-scripting-dialect.md:51-80`（权衡一）、`:182` | 判定判据过宽、倾向**收窄**并采纳为实施项，仅剩「精确边界」待定；其支撑事实「本 fork 的编译器测试树已裁剪，树内没有任何断言该禁令的测试，收窄不会与既有断言冲突」 | **一致**（本 issue 即边界收口 = 上面第 1–3 条），但**支撑事实已被后续工作取代**：崩溃修复轮新增了断言该禁令的编译器测试（见「改动面」），收窄现在必须改这些断言 |
| `proposals\README.md:43` | 把「§7 显式 `Me` 系禁令范围收窄」登记为 supersede 口径、实施项另立跟踪 | 保持；该行的收口指针指向本 issue |

## 修复方向（候选，未拍板）

**方向 A（主推）：把判据从「容纳类型是脚本类」换成「这段代码属于顶层代码」。** 现成谓词 `Binder.BindingTopLevelScriptCode`（`Binder.vb:428-443`）的 `Case SymbolKind.Method` 分支给的就是这个判据（`method.IsScriptConstructor OrElse method.IsScriptInitializer`）。命中则报 BC36966，不命中则**落到下面那段普通判据**（共享 ⇒ BC30043/BC30369；否则放行）。

**该谓词不能照抄（两处实锤）**：
- **字段/属性初始化器不被它覆盖**：初始化器的绑定用 `DeclarationInitializerBinder`，其 `ContainingMember` 逐字是字段或属性符号（`Binding\DeclarationInitializerBinder.vb:49-58`，注释自述「the binding context for an initialization needs to be the field or property symbol」）⇒ 落 `Binder.vb:439-440` 的 `Case Else` → `False`。若照抄，`Dim x As Integer = Me.counter`（探针 L，今天 BC36966）会被放开——而按判据第 1 条它属于顶层代码，**必须仍然禁**。
- **lambda 体不被它覆盖**：lambda 体内的 `ContainingMember` 是 `LambdaSymbol`，上游注释在同一函数里就点名了这一点（`Binder_Expressions.vb:1895-1898`：「containingMember will be a LambdaSymbol rather than a symbol for constructor」）。**判别性实测**：顶层语句里书写的 lambda 中的 `Return 42` **编译无 BC36966**（探针 `N-lambda-return-in-toplevel.vbx`）——而 `Return` 半条正是由该谓词把守（`Binder_Statements.vb:5113-5114`），可见该谓词在顶层 lambda 内即为 `False`。照抄它会把探针 J（顶层语句里的 lambda，今天 BC36966）一并放开，违反判据第 1 条。⇒ 判据必须**沿绑定链上溯到最近的非 lambda 容纳成员**再判；仓内没有现成的这类谓词（`Compilers\VisualBasic\Portable\` 下 `Case SymbolKind.LambdaSymbol` 零命中），要自己写这一层。两处形状（顶层 lambda、顶层字段初始化器）都要进回归锁。
- lambda 也不能改按词法位置判：顶层 `Sub` 体内的 lambda（探针 I）与顶层语句内的 lambda（探针 J）今天同报 BC36966，判据必须把前者放开、把后者留住——只有「最近的容纳成员」这一层能区分。判据若写成「源码树是否顶层树」或「是否 `IsGlobalStatement`」会在这两格上同时判错。
- 与同族的 `ERR_BadAwaitInSharedInitializer`（BC37341，`spec:239`）共用「容纳类型是脚本类」这一形状，但那条**本来就要覆盖整个脚本类**，不随本 issue 改。

**`MyBase` 的基类型（2026-09-22 改判：取到 `Nothing` 是预期行为）**。本条登记时曾定案"成员体内 `MyBase` 按 `System.Object` 解析"，并据此在 `BindMyBaseExpression` 的绑定路径上加了 `GetBaseTypeOfScriptClass` 兜底 + 四处测试。**该定案已被 C# 实测推翻**：C# 在 `Compilers\CSharp\Portable\Symbols\Source\ImplicitNamedTypeSymbol.cs:52-57` 用注释明写"脚本类返回 null 基类型，正是为了不让它继承 `ToString` / `GetHashCode`"，并在 `Compilers\CSharp\Test\Symbol\Symbols\ImplicitClassTests.cs:63`、`:76` 把**非提交**脚本类也断成无基类型、裸 `ToString` 无符号。作者据此裁定「遇到冲突按 C# script 实际策略来定，`MyBase` 取到 `Nothing` 成为了预期行为」。**处置**：兜底与其测试回退、规范措辞已改（`spec\spec-scripting-dialect.md:246`），回退与"反向分叉"（VB 按 `TypeKind.Submission` 判、C# 按 `IsScriptClass` 判 ⇒ VB 非提交脚本类多继承了 `Object` 成员）由 `issues\issue-submission-member-inherited-object-lookup.md`（issue 29 改判后）与 `tasks\submission-object-member-lookup\` 承接。本条其余判据（顶层代码禁、成员体放行、`Shared` 落 BC30043/BC30369）**不受影响**。

**改动面**：`Binder_Expressions.vb:2257-2289` 一处。随判据改期望值的既有断言（都锁在「成员体也拒」的现状上）：`Compilers\VisualBasicSemanticTest\Semantics\ScriptSemanticsTests.vb:588`（`TopLevelExplicitMe_ReportsKeywordNotAllowedInScript`，顶层 `Sub` 体内 `Me.sx`）、`:713`（顶层实例 `Sub` 体内 `MyBase`）、`:724`（顶层 `Shared Sub` 体内 `MyBase`），`Compilers\VisualBasicEmitTest\CodeGen\CodeGenScriptTests.vb:94-95`、`:115-116`，`Scripting\VisualBasicTest\ScriptModeStatementConformanceTests.vb:487-492`（顶层 `Function` 体内 `Me`）、`:497-502`（同位置 `MyClass`）。**不需改**：`Scripting\VisualBasicTest\ScriptModeParserArmConformanceTests.vb:174-181` 只断言顶层语句与顶层字段初始化器两格（判据第 1 条保持），但其文档注释 `:167-169` 与 `ScriptModeStatementConformanceTests.vb:483-484` 的注释按旧范围叙述 ⇒ 随实现改写注释。

## 修复后会新增的行为变化与须补的回归用例

按 `issues\README.md` 的清单纪律，此处只列**今天静默或今天报错、修复后改变**的形状：

| # | 形状 | 今天 | 修复后 | 须补用例 |
|---|---|---|---|---|
| 1 | 顶层实例 `Function`/`Property` 体内 `Me.字段`（有/无局部遮蔽） | BC36966 | **合法**，取字段 | 正例：遮蔽存在时取到字段值（探针 B/M 形状） |
| 2 | 顶层 `Sub` 体内 lambda 里的显式 `Me` | BC36966 | **合法** | 正例（探针 I 形状） |
| 3 | 顶层 `Shared` 成员体内显式 `Me`/`MyClass`/`MyBase` | BC36966 | **BC30043** | 负例换码（探针 E:24 形状）+ 普通类同形状对照（G1）不得回归 |
| 4 | 顶层语句里的 lambda、顶层字段初始化器里的显式 `Me` | BC36966 | **仍 BC36966** | 回归锁：探针 J、L 两格期望值不变（防 A 的判据陷阱把这两格放开） |
| 5 | 顶层实例成员体内 `MyClass.字段` | BC36966 | 合法（普通类实测 `M` 已证语义） | 正例 |
| 6 | 顶层实例成员体内 `MyBase.成员` | BC36966 | 不再报 BC36966；**取不到基类成员为预期**（脚本类无基类型），具体诊断真值由 issue 29 的补取证 U2 定 | 负例钉：顶层实例 `Function` 里 `MyBase.ToString()` 不得解析到 `Object` 成员；反例锁：普通类同形状仍合法（K）。原「按 `System.Object` 解析」的正例与其兜底一并回退 |

## 上游合并账本义务

`upstream-merge.md:280` **已在册**本判据所在分支：该条目记 `Compilers\VisualBasic\Portable\Binding\Binder_Expressions.vb`（§2.19 已在册）的「F08 新锚点：`:2264-2273` 的脚本类分支」，并逐字写着「**显式**引用路径（`:2266`）逐字保持」——这正是本 issue 推翻的口径，所以义务是**改写**该条目（连同 `:288` 一带的「对应设计」指向），而非新增条目。字面 `36966` / `ERR_KeywordNotAllowedInScript` 在该文件里确实零命中（已核）；`meetings\meeting-scripting-dialect.md:157`（RESOLUTION 18）要求的「新增 VB 错误码入账」连同本次的函数级分歧一并补齐。

## 两条附带观察（不并入本判据）

- **同一谓词已经放过了顶层 lambda 里的 `Return`。** `Return` 半条的判据正是 `BindingTopLevelScriptCode`（`Binder_Statements.vb:5113-5114`），而探针 N 实测顶层语句里的 lambda 写 `Return 42` 零诊断（该探针运行期因晚绑定调用形式抛 `MissingMemberException`，与判据无关；取的是**编译期无诊断**这一条）——即规范 `:272` 所说「在顶层脚本代码且不在脚本初始化器之内」这一条件，对写在顶层代码里的 lambda 当前并不成立。与本 issue 同源于「`ContainingMember` 在 lambda 体里是 `LambdaSymbol`」，但改的是 `Return` 半条的覆盖面，另立条目裁。
- **本次规范改写使 `spec-scripting-dialect.md` 行号后移**（`:247` 起 +2、`:262` 起 +9，全文件 399 → 408 行）。下游 52 处按行号引用该文件的站点已用固定偏移统一重定基（`tmp\spec-check-me\rebase-spec-anchors.pl`，12 个文件；逐条按改动前该行的原文比对确认引用确指本方言规范，未混入他篇）。**遗留**：改动前就已错位的站点不在这次算术范围内——例如 `spec:243` / `spec:258`（基线里分别落在诊断表末行与代码块内，而作者意在「隐式 `Me` 引用允许」那段，即 `:246`）、`spec:266` / `spec:300` / `spec:303` / `spec:310` / `spec:347`（基线里是空行）——它们随偏移整体平移后**仍偏同样的量**，要按语义重指得逐条读上下文，且更稳的做法是把行号锚改成语节名锚。

## 与在办文档的关系

- `issues\issue-submission-shared-member-implicit-me.md`（issue 08）——**同一函数的隐式分支**，其修法把「脚本类里没有共享代码」的前提改掉；本 issue 是同函数的显式分支。二者合起来才让该函数与普通类完全同形。
- `issues\issue-top-level-mybase-assert.md`（issue 15）——显式 `MyBase` 的「报完不崩」已修；本 issue 改变它在**成员体内**是否报错，须锁住那条不回归。
- `issues\issue-submission-member-inherited-object-lookup.md`（issue 29）——**本条解禁后新暴露**：提交类成员体内以 `Me` / `MyClass` 为接收者取继承自 `Object` 的成员报 BC30456（符号层刻意无基类型，成员查找上不到 `Object`；`MyBase` 已由 F01 在绑定路径兜住）。解禁前是 BC36966，两态皆错 ⇒ 无回退，另条裁。
- `issues\issue-script-auto-property-initializer-blocks-statement.md`（issue 30）——F01 写 A3 用例时撞见的**解析层**缺陷（顶层自动属性带初始化器后紧跟顶层语句 → BC30188），与本判据无关，已在改动前的发布版宿主上复现。
- `issues\issue-shadowing-across-submissions-reports-overload-error.md`（issue 26）——同属「顶层与类体两套行为」族：那条是跨提交同名被当重载，本条是顶层成员体被当成顶层代码。
- `spec\spec-scripting-dialect.md:16`、`:60`、`:64`、`:272`、`:299`——前者承诺顶层方法是脚本类的实例成员、字段初始化器属初始化器体，`:272` 自证 `Return` 与 `Me` 判据不同；本 issue 让实现兑现第 1 条承诺。
- `meetings\meeting-scripting-dialect.md:51-80`、`:123`、`:136`、`:182`；`proposals\proposal-scripting-dialect.md:113`。
- 探针与实测输出：`tmp\spec-check-me\A`…`N`（16 个 `.vbx`，无副作用、纯控制台），逐条源码与输出固化在 `tmp\spec-check-me\run-results-2026-09-22.txt`。

## 未复现 / 未查

- **REPL 交互形状**：实测覆盖 `.vbx` 文件执行。编译器层面两者同为 `DeclarationKind.Submission`（宿主恒 `isSubmission:=True`），**推测**同判据；跨提交（提交 1 的字段、提交 2 的顶层方法遮蔽）未测。
- Debug 构建未跑（本条无断言/崩溃面，与构建配置**推测**无关）。
- `MyBase` 与 `WithEvents`/`Handles`、`Custom Event` 访问器的交叉未测。
- 宿主对象（globals）成员的显式 `Me` 交叉形状未测——注意它与规范第 1 条的理由相关：顶层不限定名可能解析到宿主成员而非 `Me` 所指实例。
- 探针未清除，保留在 `tmp\spec-check-me\` 供验收复跑。
