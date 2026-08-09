# 覆写签名放宽 / Override Signature Relaxation

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本建议放宽覆写（`Overrides`）时的签名匹配约束，核心是**协变返回类型**：派生类的覆写方法可以返回基类返回类型的派生类型，例如 `Function Clone() As Derived Overrides Base.Clone`（基类声明 `Clone() As Base`）。这借鉴了"宽松委托"（relaxed delegates）的思路，属于继承/接口/扩展主题下的类型系统增强。

## Motivation
[motivation]: #motivation

现有 VB 要求覆写方法的返回类型与基类完全一致，导致经典模式被迫产生样板：派生类想返回 `Derived` 却只能声明 `Function Clone() As Base`，调用方每次都要向下转型。协变返回类型让覆写既能保持基类契约，又能返回更具体、更可用的类型。

期望的结果：覆写可以收紧返回类型（派生类型），消除冗余转型与样板代码，同时不破坏对基类签名调用的兼容性。

## Detailed design
[design]: #detailed-design

基类用 `MustOverride` 声明抽象成员：

```vb
' Signature (and name?) relaxation for member overriding.
' (à la relaxed delegates).
Class Base

    MustOverride Function Clone() As Base

End Class
```

派生类以**协变返回类型**覆写：返回类型 `Derived` 是基类返回类型 `Base` 的派生类型：

```vb
Class Derived
    Inherits Base

    Function Clone() As Derived Overrides Base.Clone
        ...
    End Function
End Class
```

`Derived.Clone` 的返回类型 `Derived` 可隐式转换到基类约定的 `Base`，因此满足覆写契约；而通过 `Derived` 类型变量调用 `Clone()` 时得到的是 `Derived`，无需向下转型。Anthony 以问号标注"签名（以及名字？）放宽"，暗示名字的放宽也在考量中。

## Drawbacks
[drawbacks]: #drawbacks

- 协变返回类型与泛型、接口方法的相互作用复杂，可能引入新的重载歧义。
- 覆写签名放宽一旦扩展到"名字放宽"，会打破现有"名字即签名身份"的直觉，风险高。
- 从基类引用调用时的返回类型仍是基类类型，行为差异可能让调用方困惑。

## Alternatives
[alternatives]: #alternatives

- 保持返回类型严格一致，靠泛型（`Function Clone(Of T As Base)() As T`）或在调用点转型实现"更像派生类型"。
- 只放宽返回类型（协变），不放宽参数与名字——作为最小可行改动。
- 在实现而非覆写层面提供额外方法（如 `Derived CloneDerived()`），覆写仍返回基类。

## Unresolved questions
[unresolved]: #unresolved-questions

- "名字放宽"（`Overrides Base.Clone` 之外的覆写）是否真的引入，Anthony 以问号标注未定。
- 协变返回类型是否只对引用类型（类）生效，还是也允许值类型（如 `Nothing` 边界）。
- 与接口实现的协变返回类型（见 `proposal-implicit-interface-implementation.md`）规则是否统一。
