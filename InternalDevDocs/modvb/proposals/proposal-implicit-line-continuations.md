# New Implicit Line Continuations / 新增隐式行继续

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

在更多语法位置引入隐式行继续（换行后无需 `_` 续行符），包括：`Then` 之前、`Handles` 之前、`Implements` 之前、`Function` 语句的 `)` 与 `As` 之间，并考虑允许查询子句之间夹注释。减少 `_` 样板，让长条件与长签名自然换行。

## Motivation
[motivation]: #motivation

VB 目前仅在运算符两端等少数位置允许隐式继续，否则必须写行尾 `_`。长条件表达式、长方法签名、事件/接口成员声明经常被 `_` 打断，既碍眼又易错（`_` 后不能有空格/注释）。把这些"结构上必然未结束"的位置也纳入隐式继续，可显著改善排版体验。

## Detailed design
[design]: #detailed-design

### `Then` 之前

条件表达式可以跨行，`Then` 独占一行：

```vb
If someComplexConditionExpression1 AndAlso
   (someComplexConditionExpression2 OrElse
    someComplexConditionExpression3)
Then
    ' Do stuff.

End If
```

### `Handles` 之前

事件处理器签名与 `Handles` 子句之间允许换行：

```vb
Sub Button1_Click(sender As Object, e As MouseMoveEventArgs)
    Handles Button1.MouseMove

    ' Do stuff.
End If
```

（原文此例以 `End If` 收尾，疑为笔误，应为 `End Sub`，见 Unresolved questions。）

### `Implements` 之前

接口成员声明与 `Implements` 子句之间允许换行：

```vb
Function GetEnumerator() As IEnumerator(Of T)
    Implements IEnumerable(Of T).GetEnumerator
    
    ' Get thing.
End Function
```

### `Function` 语句中 `)` 与 `As` 之间

长参数列表换行后，`As` 返回类型单独一行：

```vb
Function BuyCar(
           make As String,
           model As String,
           year As Integer,
           Optional trim As String
         )
         As Car
```

### 查询子句之间允许注释

原文以 "Maybe allowing comments between query clauses?" 标注，属候选方向——允许在 LINQ 查询子句之间插入注释解释每步意图：

```vb
' Getting customers
From customer In db.Customers
' in Illinois
Where customer.Address.State = "IL"
' with orders
Join order In db.Orders On order.CustomerId = customer.Id
' made last year
Where order.OrderDate.Year = 2023
' from most expensive to least
Order By order.Total Descending
' getting the top 10
Take 10
```

## Drawbacks
[drawbacks]: #drawbacks

- 隐式继续位置增多，解析器必须在更多语境判断"语句是否结束"，可能引入难以察觉的吞行/错界。
- `Then` 之前换行可能与"多行表达式"等既有隐式继续规则相互作用，规则叠加后心智负担上升。
- 查询子句间允许注释会改变查询表达式的词法结构，编辑器与格式化器需相应调整。

## Alternatives
[alternatives]: #alternatives

- 不做此功能：保留 `_` 续行，样板与踩坑点继续存在。
- 仅新增部分位置（如 `Then`、`Handles`、`Implements` 前），暂不处理查询子句间注释，缩小改动面。
- 用编辑器自动插入/隐藏 `_` 来缓解，而不改语言文法。

## Unresolved questions
[unresolved]: #unresolved-questions

- "查询子句之间允许注释"在原文标注为 "Maybe"，未定案。
- `Handles` 前的示例原文以 `End If` 结尾，应为 `End Sub`——疑似原文笔误，实现时应采用正确终止符。
- 各新增继续位置的确切词法规则（如 `Then` 前是否仅限"条件表达式已完整"的情形）。
