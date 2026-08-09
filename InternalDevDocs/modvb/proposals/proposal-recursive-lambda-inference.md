# 递归 / 互递归 Lambda 类型推断（Recursive Lambda Inference）

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

放宽对局部变量类型推断的若干限制，使递归 Lambda、互递归 Lambda、以及一些此前推断失败或属于"未赋值引用错误"的声明能够正确推断类型。核心包括：自引用 Lambda（形参/返回类型显式标注时可自引用）、互递归 Lambda（延迟到变量全部声明后再绑定）、`Static` 变量初始化式的类型推断，以及从方法调用返回值推断变量类型。

## Motivation
[motivation]: #motivation

当前（vanilla VB）中：

- 一个变量若引用自身（如递归 Lambda），通常无法进行类型推断，因为类型在推断过程中尚未确定。
- 在变量声明前引用它属于错误（也是明确赋值 definite assignment 错误），互递归因此无法书写。
- `Let snapshot = Snapshot.Create()` 这类从方法调用推断类型的场景应能工作。
- `Static cache = New Dictionary(Of Integer, Integer)` 应能推断出 `cache` 的类型。

本建议的目标是让这些声明正确工作。

## Detailed design
[design]: #detailed-design

### 从方法调用推断（This should work）

```vb
' This should work.
Let snapshot = Snapshot.Create()
```

`snapshot` 的类型由 `Snapshot.Create()` 的返回类型推断得到。

### Static 变量初始化式推断

```vb
' This should work correcly (infer the type of `cache`).
Static cache = New Dictionary(Of Integer, Integer)
```

`cache` 的类型由 `New Dictionary(Of Integer, Integer)` 推断得到。

### 自引用（递归）Lambda

通常无法对一个引用自身的变量做类型推断；但当 Lambda 的参数与返回类型都已显式标注时，类型推断在技术上不再必要，因此递归 Lambda 应可工作：

```vb
' Normally type-inference can't be done for a variable which
' refers to itself (e.g. a recursive lambda expression).
' But, since the types are explicit on the lambda,
' technically type-inference is unnecessary; this should work.
Let factorial =
      Function(n As Integer) As Integer
          If n < 0 Then Throw

          Return If(n = 0, 1, n * factorial(n - 1))
      End Function
```

### 互递归 Lambda

互递归需要两个 Lambda 在声明前相互引用。问题是：通常"在变量声明前引用"是错误（也是明确赋值错误），能否在"Lambda 直到所有变量都声明并初始化后才被调用"的前提下放宽这些规则，以启用互递归？

```vb
' Normally it's an error to refer to a variable before it's
' declared (and also a definite assignment error).
' Can these rules be relaxed in lambdas that aren't invoked
' until all variables are declared and initialized to
' enable mutual recursion?
Let isEven = Function(n As Integer) As Boolean
                 Return n = 0 OrElse isOdd(n - 1)
             End Function

Let isOdd = Function(n As Integer) As Boolean
                Return n <> 0 OrElse isEven(n - 1)
            End Function
```

### 规则要点

- 对"变量引用自身 / 引用后声明变量"的限制，可在 Lambda 未被提前调用（直到所有相关变量声明并初始化）的前提下放宽。
- 显式标注了参数与返回类型的 Lambda 不需要类型推断，自引用可被接受。
- 该放宽与明确赋值（definite assignment）规则的关系需要明确界定。

## Drawbacks
[drawbacks]: #drawbacks

- 放宽"声明前引用"错误可能掩盖真实的顺序错误（如不小心在赋值前使用变量）。
- 需要更复杂的声明分析，判断 Lambda 是否真的会延迟到所有变量初始化后再被调用，实现复杂、边界情况多。
- 互递归的类型绑定需在变量全部声明完成后进行，可能影响增量编译/编辑体验。

## Alternatives
[alternatives]: #alternatives

- 维持现状：递归 Lambda 需借助具名函数、或显式声明委托变量（如 `Dim factorial As Func(Of Integer, Integer)`）后再赋值。
- 引入独立的 `Function` / 本地函数语法，天然支持递归与互递归，而非放宽变量声明规则。
- 仅允许显式标注类型的 Lambda 自引用，暂不支持互递归（逐步放宽）。

## Unresolved questions
[unresolved]: #unresolved-questions

- 原文对互递归持疑问语气："Can these rules be relaxed in lambdas that aren't invoked until all variables are declared and initialized to enable mutual recursion?"
- 是否放宽互递归，还是只支持单变量自引用，未定。
- "Lambda 未被提前调用"这一前提如何静态判定，实现策略未定。
