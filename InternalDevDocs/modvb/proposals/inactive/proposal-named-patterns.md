# Named Patterns / 命名模式（解构语法）

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

命名模式把"模式即函数"的思想推到一般化：任何一个以 `Out` 参数承载子值、返回 `Boolean` 的函数都可以作为模式，并以 **构造函数调用式的解构语法** 出现在 `Select Case` 的 `Case` 子句或 `If ShapeOf x Is ...` 中，递归嵌套地匹配与拆解对象结构。模式函数可用 `Imports SyntaxPatterns` 批量引入作用域。

## Motivation
[motivation]: #motivation

手写语法树分析（如判断一个语句是否是 `Me.New(...)` 构造器调用）通常要嵌套几十行 `TypeOf` + 成员访问检查。若能把"判断节点形状并取出关键部分"封装成模式函数，再以与工厂语法互为镜像的解构写法直接书写匹配结构，分析代码就能从"过程式的深度遍历"变成"声明式的形状描述"，大大提升可读性与复用性。

## Detailed design
[design]: #detailed-design

模式函数是"带 `Out` 参数、返回 `Boolean`"的函数（见用户定义模式方法建议）。命名模式用类似构造器调用的语法去调用它并绑定其 `Out` 输出。原文示例（第 7 章）：

```vb
' Named patterns.
Function ConstructorCall(
           node As ExecutableStatementSyntax,
           Out kind As VBSyntaxKind,
           Out arguments As SeparatedSyntaxList(Of ArgumentSyntax)
         )
         As Boolean

    ' Bring shared pattern functions into scope.
    Imports SyntaxPatterns

    ' Deconstruction syntax matches/inverts factory syntax.
    Select Case ShapeOf node
      ' MyBase.New(...)
      Case ExpressionStatement(
             InvocationExpression(
               MemberAccess(
                 MeExpression(),
                 IdentifierName("New")
               ),
               argList
             )
           )
       
        Return True, kind:=VBSyntaxKind.MeExpression,
                     arguments:=argList.Arguments

      ' Me.New(...)
      Case ExpressionStatement(
             InvocationExpression(
               MemberAccess(
                 MyBaseExpression(),
                 IdentifierName("New")
               ),
               argList
             )
           )

        Return True, kind:=VBSyntaxKind.MyBaseExpression,
                     arguments:=argList.Arguments

      Case Else

        Return False, kind:=Nothing, arguments:=Nothing
      
    End Select
End Function
```

- `Function ConstructorCall(node As ExecutableStatementSyntax, Out kind As VBSyntaxKind, Out arguments As SeparatedSyntaxList(Of ArgumentSyntax)) As Boolean`：`ConstructorCall` 是一个命名模式，输入节点 `node`，输出 `kind` 与 `arguments`。
- `Imports SyntaxPatterns`：把共享模式函数引入当前作用域，供 `Case`/`If` 直接使用。
- `Case ExpressionStatement(InvocationExpression(MemberAccess(MeExpression(), IdentifierName("New")), argList))`：**解构语法与工厂语法互为镜像**——最外层 `ExpressionStatement(...)` 逐层向内，`argList` 等裸标识符是子模式的 `Out` 绑定；命中的最内层绑定结果一路向外可见。此分支匹配 `Me.New(...)`，另一分支匹配 `MyBase.New(...)`。
- `Return True, kind:=VBSyntaxKind.MeExpression, arguments:=argList.Arguments`：命中后通过 `Return` 的命名实参把子值赋值给 `Out` 参数。

模式函数还可以在 `If` 中复用。原文示例：

```vb
' Check if first statement of constructor is
' a call to another constructor.
Let firstStatement As ExecutableStatementSyntax = ...

If ShapeOf firstStatement Is ConstructorCall(kind, args) Then
    If kind = VBSyntaxKind.MeExpression Then
        ProcessChainedConstructorCall(args)
        
    ElseIf kind = VBSyntaxKind.MyBaseExpression Then
        ProcessBaseConstructorCall(args)
        
    End If
Else
    ProcessOtherStatement(firstStatement)
   
End If
```

- `If ShapeOf firstStatement Is ConstructorCall(kind, args) Then`：在 `If` 中调用命名模式，`kind`、`args` 由编译器按 `Out` 隐式声明并绑定。
- 命中后 `kind` 区分是 `MeExpression`（链式构造器调用）还是 `MyBaseExpression`（基类构造器调用），把 `args` 交给后续处理；`Else` 分支处理其他语句。

## Drawbacks
[drawbacks]: #drawbacks

- 解构写法与真实工厂/构造器调用在语法上极易混淆，编译器需靠"函数是否可作为模式"来消歧，对解析器与用户都增加认知负担。
- 递归嵌套模式一旦写错一层，错误信息难以定位到具体哪一层的哪个参数。
- "每个模式都是返回 `Boolean` 的函数"的约定松散，缺少机制保证模式只读输入、只在成功时写 `Out`。

## Alternatives
[alternatives]: #alternatives

- 沿用过程式写法：`If TypeOf firstStatement Is ExpressionStatementSyntax Then ...` 逐层下钻。
- 用专门语法声明模式（如 `Pattern` 关键字/特性），使"可作为模式"成为显式契约，而不是对任意函数的约定。
- 由编译器为类型自动生成解构模式（如根据构造函数参数生成），减少手写模式函数的样板。

## Unresolved questions
[unresolved]: #unresolved-questions

- 裸标识符（如 `argList`）在解构语法中的绑定规则：重复出现时是约束相等还是新建变量？原文未说明。
- `Imports SyntaxPatterns` 是一次引入一组函数还是单个；命名空间 `SyntaxPatterns` 是否为约定俗成的集中地。
- 模式之间能否做"或"（`Case A(...) Or B(...)`）、"且"、取反等组合；原文只展示了顺序 `Case` 与 `Case Else`。
- 递归嵌套的深度是否有限制，嵌套模式的最内层是否只能绑定叶子值（如字符串字面量 `"New"`）。
