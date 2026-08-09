# TypeOf ... Is 类型流分析（TypeOf Flow Analysis）

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

对 `TypeOf ... Is` / `TypeOf ... IsNot` 测试进行类型流分析（type flow analysis）：当某个变量经过一次类型测试后，编译器在后续代码中把该变量的静态类型收窄为被测类型，从而无需强制转换即可访问被测类型的成员。

## Motivation
[motivation]: #motivation

当前（vanilla VB）中，`If TypeOf animal Is Duck Then` 之后变量 `animal` 的静态类型仍然是基类型，因此直接调用 `animal.Quack()` 会报错，用户必须重复书写强制转换。对于深度多态的场景（如动物分类学示例），这造成大量样板代码。

期望的结果：类型测试产生"流敏感"的效果——在真分支、假分支、循环体、守卫语句之后，编译器知道变量的实际（收窄后的）类型，并允许访问该类型的成员，同时不改变传统的运行时语义。

## Detailed design
[design]: #detailed-design

### If 语句中的类型收窄

当 `If` 条件为 `TypeOf animal Is Duck` 时，其分支内 `animal` 的类型被收窄为 `Duck`：

```vb
If TypeOf animal Is Duck Then
    animal.Quack()
End If
```

收窄同样适用于嵌套的 `If`，内层测试进一步收窄外层已收窄的类型（`Vertebrate` → `Reptile` → `Snake`）：

```vb
If TypeOf animal Is Vertebrate Then
    animal.FlexBackbone()

    If TypeOf animal Is Reptile Then
        animal.ShedScales()

        If TypeOf animal Is Snake Then
            animal.Coil()
        End If
    End If
End If
```

### Select Case TypeOf

`Select Case TypeOf animal` 中每个 `Case` 分支对该类型做一次收窄：

```vb
Select Case TypeOf animal
    Case Mammal
        animal.SecreteMilk()

    Case Bird
        animal.LayEggs()

    Case Fish
        animal.Swim()

    Case Insect
        animal.Creep()

    Case Else
        Throw New NotImplementedException()

End Select
```

`Select Case TypeOf` 也支持 `Case IsNot` 反向匹配（`dinosaur` 在真分支中**不是** `Bird`）：

```vb
Select Case TypeOf dinosaur
    Case IsNot Bird

        dinosaur.GoExtinct()

    Case Else
        ' This metaphor is straining.
        If dinosaur.IsDodo Then
            dinosaur.GoExtinct()
        Else
            ' Expensive O(n) lookup on Wikipedia.

        End If

End Select
```

### AndAlso / OrElse 组合

在 `AndAlso` 右侧可访问左侧类型测试已收窄的成员：

```vb
If TypeOf animal Is Bird AndAlso animal.WingspanInMeters > 2 Then
    ' Big Bird
End If
```

当多个 `TypeOf ... Is` 以 `AndAlso` 组合时，交集处同时具备两种类型的成员：

```vb
If TypeOf animal Is Mammal AndAlso TypeOf animal Is ICanFly Then
    ' Members of both types are available.
    animal.SecreteMilk()
    animal.Fly()
End If
```

`OrElse` 组合配合 `IsNot` 测试同样生效（`animal.IsMonotreme` 依赖对 `animal` 的收窄）:

```vb
' Monotremes are egg-laying mammals, including the Platypus and the Echidna.
If TypeOf animal IsNot Mammal OrElse animal.IsMonotreme Then
    mayLayEggs = True
End If
```

### IsNot 守卫语句（Return / Exit / Continue / Throw）

若 `If ... Then Return`（同样适用于 `Exit`、`Continue`、`Throw`）作为守卫，则守卫之后的代码中该变量必然满足相反的测试。这里 `animal` 必然不是非 `Mammal`，即它一定是 `Mammal`：

```vb
' Works with guard statements (Return, Exit, Continue, Throw).
If TypeOf animal IsNot Mammal Then Return

animal.ShedHair()
```

### If() 表达式中的收窄

`If()` 的每个操作数内同样进行类型流分析：

```vb
Let holders = If(TypeOf quadruped Is Bird,
                 quadruped.Hindlimbs,
                 quadruped.Forelimbs)
```

真分支中 `quadruped` 被收窄为 `Bird`，其成员 `Hindlimbs` 可直接访问。

### 循环中的收窄

循环结束时，`Do Until` / `Do While` 的条件保证循环后变量的类型：

```vb
Let animal As Animal = GetDolphin()

Do Until TypeOf animal Is ILandDwelling
    ' Or maybe I should have said:
    ' Do While TypeOf animal IsNot ILandDwelling?
    animal = animal.GetImmediateAncestor()
Loop

animal.Walk()
```

循环体每次迭代时都会更新（`animal = animal.GetImmediateAncestor()`），因此循环内的收窄以每次迭代重新计算。

### 查询表达式（LINQ）中的收窄

`Where` 等查询子句中的 `TypeOf ... Is` 亦可收窄后续子句中的类型：

```vb
Let usStateBearsByHibernationPeriod =
    From
        state In usStates
    Let
        mammal = state.StateMammal
    Where
        TypeOf mammal Is Bear AndAlso Not mammal.IsExtinct
    Order By
        mammal.HibernationPeriod Descending
```

### 边界情况

- 类型收窄不改变运行时语义，只影响编译期的成员解析与报错。
- 收窄与 `AndAlso` / `OrElse` / 守卫语句的相互作用需要明确的空、确定性分析（definite assignment）。
- 类型测试的对象被重新赋值后（如循环体内），收窄必须重新计算。

## Drawbacks
[drawbacks]: #drawbacks

- 编译器需要维护流敏感的类型信息，增加实现复杂度与编译时间。
- 收窄规则若不严谨，可能产生"看似收窄、实为错误的类型"，反而误导使用者。
- 与既有晚期绑定（late binding）语义需要明确区分，避免改变现有程序的解析结果。

## Alternatives
[alternatives]: #alternatives

- 不做流分析，维持现状：用户必须手动 `CType(animal, Duck)`，产生大量样板代码。
- 借助通用模式匹配（如 `ShapeOf`）统一实现类型测试与收窄，但这属于更大的语言变更。
- 仅收窄到"显式声明的局部变量"，不覆盖属性/字段，减少实现面。

## Unresolved questions
[unresolved]: #unresolved-questions

- 循环场景采用 `Do Until TypeOf animal Is ILandDwelling` 还是 `Do While TypeOf animal IsNot ILandDwelling` 更合理？原文注释："Or maybe I should have said: Do While TypeOf animal IsNot ILandDwelling?"
- `If()` 表达式内两个操作数的收窄范围（各自的真/假分支）的精确规则待定。
- 类型收窄在多分支赋值、闭包捕获等场景下的交互规则未完全确定。
