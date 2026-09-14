# 脚本顶层显式 `MyBase` → `BoundMyBaseReference` 的非空断言（提交类没有基类型）

* 状态：**Fixed**（已验证，commit 待作者提交后补）
* 发现日期：2026-09-13
* 发现场景：崩溃形状穷尽扫描（`tmp\probes\sweep\`，390 探针）

## 触发面

脚本顶层的**显式** `MyBase`，三种位置同症状——顶层裸表达式、顶层实例 `Sub` 体内、顶层 `Shared Sub` 体内：

```vbx
Console.WriteLine(MyBase.ToString())
```

## 症状与实测退出码

断言终止，`EXITCODE=2148734499`，栈顶是构造 `BoundMyBaseReference` 时的 `Field 'type' cannot be null`：

```
Debug.Assert(type IsNot Nothing, "Field 'type' cannot be null
(use Null=""allow"" in BoundNodes.xml to remove this check)")
```

（`Compilers\VisualBasic\Portable\Generated\BoundNodes.xml.Generated.vb:6016`，两个构造重载各一处：`:6016` / `:6023`。）

## 根因

**错误路径要先取到基类型才能构造错误节点，而提交类没有基类型。**

`Binding\Binder_Expressions.vb` 的 `BindMyBaseExpression` 在 `CanAccessMyBase` 为假时先报诊断，再构造 `BoundMyBaseReference` 作为兜底：

- 正常路径（`:2379`）对 `Me.ContainingType` 为 `Nothing` 已写 `ErrorTypeSymbol.UnknownResultType` 兜底；
- **错误路径**却直接取 `Me.ContainingType.BaseTypeNoUseSiteDiagnostics`——提交类的该值为 `Nothing`（提交类没有基类型），于是取到 `Nothing` 的 `type` 撞断言。

判别性依据：同文件 `BindMeExpression`（`:2351`）与 `BindMyClassExpression`（`:2387`）的同类错误路径用的是 `If(Me.ContainingType, ErrorTypeSymbol.UnknownResultType)` 兜底——缺口只在这一处。

## 预期行为

`BC36966`（`ERR_KeywordNotAllowedInScript`）**本来就已报出**（见 `spec\spec-scripting-dialect.md` 的脚本专属诊断表），缺的只是「报完不崩」。即：**保留已报的诊断，补上兜底**。

普通上下文的同形状（对照）：

| 上下文 | 行为 |
|---|---|
| `Class C : Inherits B` 里的 `MyBase.ToString()` | **合法**，`vbc.exe` exit `0`，运行输出 `B`（实测 `tmp\probes\u6bc\mybase-ordinary-class.vb`） |
| `Module` 里的显式 `MyBase` | `BC32001` |
| `Structure` 里的显式 `MyBase` | `BC30044` |

⇒ 判定**修好**（按已报诊断收口），不新增诊断码。

## 修复方向

错误路径的类型形参沿用同一文件 `BindMeExpression` 已有的兜底写法，把「取不到基类型」折成 `ErrorTypeSymbol.UnknownResultType`（`Binder_Expressions.vb:2374-2375`）。正常路径（`:2379`）不动。

## 修复后的新增行为变化

**无新增诊断，无产物形状变化**——`BC36966` 从「报完即崩」变为「报完即止」。实测（当前编译器）：

```
error BC36966: 您不能使用顶级脚本代码中的“MyBase”
```

exit `1`，**没有** `Process terminated`（`tmp\probes\u6bc\mybase-expr.vbx`）。普通 `Module` / `Structure` / 有基类的 `Class` 三种对照的诊断与产物不变。

回归用例：`Compilers\VisualBasicSemanticTest\Semantics\ScriptSemanticsTests.vb` 的同族用例，断言诊断 id 与「不抛异常」。

## 相关

- 同为「顶层形状落到合成提交类上撞不变量」的姊妹问题：`issue-submission-instance-constructor-crash.md`（12）、`issue-top-level-raise-event-instance-event-crash.md`（13）、`issue-top-level-handles-clause-crash.md`（14）。
