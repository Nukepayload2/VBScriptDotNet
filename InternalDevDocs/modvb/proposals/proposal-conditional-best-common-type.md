# 条件表达式（If()）的最佳公共类型推断（Best Common Type for If()）

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

改进 `If(condition, a, b)` 条件表达式的类型推断：当第二个与第三个操作数无法互相转换时，不再是简单地推断为 `Object`，而是推断为两者的最近公共基类型（best common type），从而在声明的变量上可直接访问公共基类型定义的成员。

## Motivation
[motivation]: #motivation

在 vanilla VB 中，类型推断要求第 2、3 个操作数能转换为彼此的类型之一，否则推断失败，结果为 `Object`。于是 `If(someCondition, New Cat, New Dog)` 被推断为 `Object`，只能进行晚期绑定访问，既慢又易错：

```vb
' In vanilla VB, type inference requires the 2nd & 3rd operands
' to convert to the type of one or the other or type inference
' fails, resulting in type 'Object'.
Dim animal = If(someCondition, New Cat, New Dog)
```

期望的结果：推断出两个操作数的最近公共基类型，使声明变量在编译期即可访问公共成员。

## Detailed design
[design]: #detailed-design

在 ModVB 中，`If(someCondition, New Cat, New Dog)` 的推断结果不再是 `Object`，而是两个类型的最近公共基类型。在原文的动物学示例中，该公共类型是 `Carnivora`（食肉目——猫、犬类胎盘哺乳动物的共同分类阶元）。不过作者也指出，即使推断出 `Carnivora`，示例里实际能用的成员仍定义在 `Mammal` 上，但这已经比 `Object` 可用的成员多得多：

```vb
' In ModVB, 'animal' would have type 'Carnivora',  which is the taxonomical
' order common to cat-like and dog-like placental mammals, but that's not
' actually useful because not all carnivorans are carnivores, e.g. bears
' are often omnivores except for the Giant Panda which mostly eats plants.
' So, even though it's inferred that type, the only members I can use are
' defined on 'Mammal' in this example, which is still more than I could
' use if the type were 'Object', so...
Let animal = If(someCondition, New Cat, New Dog)
animal.SecreteMilk()
```

### 规则要点

- 推断结果是第 2、3 操作数静态类型的最近公共基类型（可包括接口）。
- 该类型不一定是最直观的公共类型（如 `Carnivora` 而非 `Mammal`），但只要它包含示例所需成员（`SecreteMilk` 定义于 `Mammal`），即为可用结果。
- 相比推断为 `Object`，公共基类型在编译期即可解析成员，避免晚期绑定。
- 若两者无公共基类型，仍回退到 `Object`。

## Drawbacks
[drawbacks]: #drawbacks

- 最近公共基类型的计算（特别是包含多个继承层次、接口时）可能得到"不够直观"的结果，如示例中的 `Carnivora`，反而不如显式声明更可读。
- 改变推断结果属于破坏性变更：原本编译为 `Object`（晚期绑定）的代码可能改为编译期绑定，导致个别成员解析差异。
- 推断结果可能因类型层次变化而变化，增加对公共 API 的隐式依赖。

## Alternatives
[alternatives]: #alternatives

- 维持推断为 `Object` 的现状，让用户显式写出目标类型（`CType` / 类型注释）。
- 允许为 `If()` 提供显式类型参数，由用户指定公共类型。
- 采用其他"最小上界"（least upper bound）算法，如优先选择单个继承类而非接口。

## Unresolved questions
[unresolved]: #unresolved-questions

- 最近公共基类型的具体算法（含多接口、泛型实例化、类型参数）未完全确定。
- 推断出的公共类型仅在"恰好包含所需成员"时可用（原文示例中 `SecreteMilk` 定义在 `Mammal`，而非推断出的 `Carnivora`），作者对"推断类型 ≠ 可操作类型"的现实是否可接受持保留态度。
- 是否应提供 `If()` 的显式类型参数作为兜底，待定。
