# 无类型声明的默认类型 / Default Type for Typeless Declarations

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

"无类型"（typeless）声明（如 `Function Add(left, right)`、`Sub New(ParamArray args())`、`Property Description`、`Async Function Fetch(objectId)`、`Private State`）默认使用 `Any` 动态类型，而非编译期检查的 `Object`。适合 RAD/原型开发，支持渐进类型化。

## Motivation
[motivation]: #motivation

- 快速原型/RAD 场景常省略类型（`Function Add(left, right)`），此时参数、返回值与字段需要一个默认类型；
- 若默认是编译期检查的 `Object`，成员访问会报错，削弱了省写类型的意义；
- 以 `Any`（动态）为默认，写原型时不加类型也能自由使用，之后逐步补上显式类型即实现渐进类型化。

## Detailed design
[design]: #detailed-design

### 无类型声明默认 `Any`

```vb
' New default type for "typeless" declarations.
' Great for RAD/prototypes with gradual typing.
Function Add(left, right)
'Function Add(left As Any, right As Any) As Any
    
Sub New(ParamArray args())
    
Property Description
    
Async Function Fetch(objectId)

Private State
```

下列无类型声明均隐式采用 `Any`：

- `Function Add(left, right)`：两个形参 `left`、`right` 与返回值均默认 `Any`（注释给出了等价展开 `Function Add(left As Any, right As Any) As Any`）；
- `Sub New(ParamArray args())`：构造函数的 `ParamArray` 形参数组元素类型默认 `Any`；
- `Property Description`：无类型属性的 `Get`/`Set` 类型默认 `Any`；
- `Async Function Fetch(objectId)`：异步函数的形参与返回类型默认 `Any`（异步返回值按 `Task(Of Any)` 处理）；
- `Private State`：字段声明省略类型时默认为 `Any`。

渐进类型化：原型阶段不加类型直接用，后续需要类型安全时补上 `As SomeType` 即可，编译器行为随之从动态转为静态。

## Drawbacks
[drawbacks]: #drawbacks

- 无类型默认 `Any` 意味着大量动态分发，运行时开销与晚期绑定错误风险上升。
- 对"忘记写类型"的笔误不再报错，可能掩盖真正的类型错误。
- 与现有 VB 中 `Option Explicit Off` 行为、无类型默认 `Object` 的既有代码形成兼容性分叉。

## Alternatives
[alternatives]: #alternatives

- 维持无类型默认 `Object`（现状），仅靠 `Option Strict Off` 获得宽松绑定；
- 默认仍为编译期检查类型，要求原型也必须显式声明；
- 提供配置项/`Option` 控制无类型默认是 `Any` 还是 `Object`。

## Unresolved questions
[unresolved]: #unresolved-questions

- 无类型默认 `Any` 的适用范围：是否仅函数/属性/字段，还是也涵盖局部变量与 `ParamArray`。
- `Async Function` 无类型返回的精确处理：`Task(Of Any)` 如何与 `Await` 交互。
- `Property Description` 的 `Get`/`Set` 是否必须类型一致（同为 `Any`）。
- 与 `Option Strict On`/`Option Infer` 的关系：这些开关开启后是否仍允许无类型默认 `Any`。
- 渐进类型化的迁移工具与警告策略（何时提示补类型）。
