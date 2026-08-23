# Consuming C# Interface Shared Members

* [x] Proposed
* [x] Prototype: Complete
* [x] Implementation: Complete
* [x] Specification: [Complete](spec-consume-interface-shared-members.md)

## Summary
[summary]: #summary

This specification defines how the Visual Basic language consumes C# 11 static abstract interface members — shared abstract members declared directly on an interface. The language binds `T.Member`, `T.Property = value`, `AddressOf T.Member`, `NameOf(T.Member)`, and operators through a type parameter constrained to the declaring interface, and emits them as constrained calls so that the run time dispatches on the actual type argument.

The specification covers consumption only. Visual Basic has no declaration syntax for these members: a `Shared` member declared directly on an interface is not declared by the language. The boundary is drawn explicitly and is not changed by this specification.

The C# equivalent is specified in the [static abstracts in interfaces][static-abstracts] proposal for C# 11. This specification describes the Visual Basic side of the same metadata contract. The consumption of C# 14 extension members is the subject of a separate specification.

## Motivation
[motivation]: #motivation

.NET generic math — `INumber(Of T)` and the interfaces built on it — is implemented with static abstract interface members. A Visual Basic program can use a concrete type's static member (`MyNum.Zero`) and can pass the type to a C# generic method, but it cannot write the generic algorithm itself: `T.Zero` is rejected because a type parameter cannot be used as a qualifier. The ability to write such algorithms is the gap this specification closes.

Because the interactive window and ordinary compilation share a single implementation of the language, the consumption rules must be identical in both modes.

## Detailed design
[design]: #detailed-design

The feature introduces no new syntax. It extends the mechanism the language already uses — access to shared members through a type name — to the type-parameter qualifier.

Throughout this section, the examples reference interfaces that `ExternLib` declares: `IHasZero(Of T)` with `static abstract T Zero { get; }` and `static abstract T Add(T a, T b)`, `IConfig(Of T)` with `static abstract T Current { get; set; }`, and `IAddable(Of T)` with `static abstract T operator +(T, T)`.

### Representation in metadata

A static abstract interface member is a shared abstract member declared directly on an interface — `static abstract` in C#. In metadata it is a static abstract method, or the accessor of a static abstract property. A static virtual interface member (`static virtual` in C#) is shared but not abstract: it carries a default implementation.

The language recognizes a **static abstract interface member** as a shared member that is abstract and whose containing type is an interface. The recognition applies to methods and to property accessors alike.

### Binding through constrained type parameters

`T.Member`, where `T` is a type parameter, binds to the shared members of the **effective interface set** of `T` — the interface constraints of `T` together with all interfaces those interfaces inherit. The binding is restricted to static abstract interface members.

```vbnet
Imports ExternLib

Function Sum(Of T As IHasZero(Of T))(items As T()) As T
    Dim result As T = T.Zero                  ' Okay: static abstract property getter
    For Each item In items
        result = T.Add(result, item)          ' Okay: static abstract method
    Next
    Return result
End Function
```

The existing rule that a type parameter cannot be used as a qualifier remains in force for every other case: a member access through a type parameter that does not resolve to a static abstract interface member of the effective interface set reports BC32098. This covers an unconstrained type parameter, a type parameter constrained only to a class, a name that no interface in the effective interface set declares, and a shared member that is not abstract.

```vbnet
Function Use(Of T)(x As T) As Object
    Return T.Zero                             ' Error BC32098: type parameters cannot be used as qualifiers
End Function

Function Use(Of T As MyBaseClass)(x As T) As Object
    Return T.Zero                             ' Error BC32098: the class constraint contributes no interface members
End Function
```

**Decision**: Binding through a type parameter is confined to static abstract members. A static virtual member — shared but not abstract — is not bound through a type parameter and reports BC32098. This is narrower than C#, which also dispatches static virtual members through a type parameter; the specification deliberately restricts the surface to the abstract form, whose run-time dispatch does not depend on a default implementation.

The property setter is symmetric with the getter:

```vbnet
Imports ExternLib

Sub Configure(Of T As IConfig(Of T))(value As T)
    T.Current = value                         ' Okay: static abstract property setter
End Sub
```

### Runtime support

Consuming a static abstract interface member requires the target runtime to support them. Support is determined by the presence of `System.Runtime.CompilerServices.RuntimeFeature.VirtualStaticsInInterfaces`. When the member comes from a referenced module and the target runtime does not provide that feature member, the use reports BC32134.

```vbnet
' Target runtime without static abstract interface member support:
Function Sum(Of T As IHasZero(Of T))(items As T()) As T
    Dim result As T = T.Zero                  ' Error BC32134: the target runtime does not support static abstract members in interfaces
End Function
```

### Code generation

A call to a static abstract interface member through a type parameter is emitted as `constrained.` followed by `call`, with the type parameter as the constrained type. It is not emitted as `callvirt`, and the receiver is not dropped: the constraint tells the run time to resolve the member against the actual type argument. This matches the C# emission for the same construct.

```vbnet
Imports ExternLib

Function Sum(Of T As IHasZero(Of T))(items As T()) As T
    Dim result As T = T.Zero
    For Each item In items
        result = T.Add(result, item)
    Next
    Return result
End Function
```

The calls to `get_Zero` and `Add` inside this body are emitted as `constrained. !!T` + `call`.

### Operators

An operator whose operands are type parameters resolves to a static abstract operator declared on the effective interface set. The compiler collects the shared operators of the effective interface set alongside the operator candidates gathered from the operand types themselves, and the existing operator-resolution machinery selects among them. A successful resolution is emitted as `constrained.` + `call`; the emitted IL is byte-for-byte identical to the C# emission.

```vbnet
Imports ExternLib

Function AddAll(Of T As IAddable(Of T))(a As T, b As T) As T
    Return a + b                              ' Okay: static abstract operator through type parameters
End Function
```

A static abstract operator is not consulted for an interface-typed operand that is not a type parameter, and a static virtual operator is not collected as a candidate at all. In both cases the ordinary operator-resolution failure is reported (`Operator '...' is not defined`).

### Delegate creation and nameof

`AddressOf T.Member` creates a delegate whose target is the static abstract member. The delegate-creation is emitted as `constrained.` + `ldftn`, and the type-expression receiver is preserved for the emission. The delegate is closed over the type argument, not over an instance.

```vbnet
Imports ExternLib

Function GetCombiner(Of T As IHasZero(Of T))() As Func(Of T, T, T)
    Return AddressOf T.Add                    ' Okay: delegate creation; emitted as constrained. + ldftn
End Function
```

`NameOf(T.Member)` is accepted and returns the member name. The `NameOf` context suppresses the errors that a direct access would report, because `NameOf` needs only the symbol and never evaluates the access.

```vbnet
Imports ExternLib

Function GetMemberName(Of T As IHasZero(Of T))() As String
    Return NameOf(T.Add)                      ' Okay: nameof
End Function
```

### Expression trees

A static abstract interface member cannot be represented in an expression tree. When a lambda that references one is converted to an expression tree — whether through a method call, a property access, or an `AddressOf` inside the lambda — the expression-tree rewriter reports BC37340. This is aligned with the C# CS8927.

```vbnet
Imports ExternLib
Imports System.Linq.Expressions

Class Test
    Shared Sub Make(Of T As IHasZero(Of T))()
        Dim f = CType(Function() AddressOf T.Add, Expression(Of Func(Of Func(Of T, T, T))))   ' Error BC37340
    End Sub

    Shared Sub Make(Of T As IConfig(Of T))(value As T)
        T.Current = value                     ' Okay: ordinary assignment, no diagnostic
        Dim s = CType(Sub() T.Current = value, Expression(Of Action))   ' Error BC37340
    End Sub
End Class
```

An ordinary — non-expression-tree — `AddressOf` or assignment reports no diagnostic, because the delegate-creation and assignment paths carry the type-parameter receiver and emit the constrained form directly.

### Direct access through an interface name

A static abstract or static virtual interface member accessed directly through its interface name is an error. Such a member is reachable only through a constrained type parameter. Consider a non-generic C# interface `I1` that declares `static abstract void M01();` and `static abstract int Zero { get; }`:

```vbnet
Class Test
    Shared Sub Goo()
        I1.M01()                              ' Error BC37314: a shared abstract or virtual interface member cannot be accessed
        Dim z As Integer = I1.Zero            ' Error BC37314
    End Sub
End Class
```

This is aligned with the C# CS8926. The error is not reported for a `NameOf` argument, which does not access the member, and it is not reported when the member is reached through a type parameter.

### Boundaries

The specification does not give the language declaration syntax for these members. A `Shared` member declared directly on an interface is still rejected at the declaration, for a method with BC30270 and for a property with BC30273.

```vbnet
Interface I1
    Shared Sub M1()                           ' Error BC30270: 'Shared' is not valid on an interface method declaration
End Interface

Interface I2
    Shared Property P1 As Integer             ' Error BC30273: 'Shared' is not valid on an interface property declaration
End Interface
```

Declaring these members remains a future concern, separate from this specification.

## Drawbacks
[drawbacks]: #drawbacks

- **Asymmetry with C# for static virtual members.** C# dispatches static virtual members through a type parameter; this specification binds only static abstract members and reports BC32098 for the static virtual form. Code that C# accepts is rejected by Visual Basic. The asymmetry is a deliberate restriction of the surface, not a modeling of C#.

- **Runtime support requirement.** A use of a static abstract interface member requires the target runtime to provide `RuntimeFeature.VirtualStaticsInInterfaces`. A program that consumes such a member fails to compile against a target runtime that lacks the feature rather than producing invalid IL. Libraries that use the feature therefore constrain the runtimes their consumers can target.

## Alternatives
[alternatives]: #alternatives

- **Bind static virtual members through type parameters, as C# does.** This was considered and set aside. Dispatching a static virtual member relies on the run time selecting the actual type's implementation, and the specification confines the new surface to the abstract form for which dispatch is well-defined. Extending to static virtual members is a compatible future change.

- **Expose a syntax for declaring the members.** Declaring shared interface members in Visual Basic source is a separate, larger feature that would add syntax to the language. The consumption-side value stands alone, and the declaration side is deliberately out of scope.

## Unresolved questions
[unresolved]: #unresolved-questions

None.

## Considerations
[considerations]: #considerations

### Runtime support

Static abstract interface members require run time support that is not present on every target. The feature gates consumption on `System.Runtime.CompilerServices.RuntimeFeature.VirtualStaticsInInterfaces`, so a program that consumes such a member fails to compile against a target runtime that lacks the feature rather than producing invalid IL.

### API versioning

Because Visual Basic only consumes these members, the versioning surface is entirely on the C# side. A library that adds a static abstract interface member changes which Visual Basic programs compile against it: a use that previously reported an error now binds. A library that removes one changes a previously valid use into an error. Neither direction affects the meaning of a program that does not name the member.

### Expression trees and `NameOf`

`NameOf` requires a resolvable symbol but must not require the access to be evaluable. The specification suppresses the direct-access error for `NameOf` arguments and accepts `NameOf(T.Member)`. The expression-tree restriction, by contrast, is about the tree representation: a static abstract member has no tree node form, so any reference inside a lambda converted to an expression tree is an error, including a reference in a `NameOf` that would be legal in ordinary code.

### Alignment with C#

The code generation is aligned with C#: a static abstract member through a type parameter is emitted as `constrained.` + `call`, a delegate creation as `constrained.` + `ldftn`, and the emitted IL for operators is byte-for-byte identical. The error codes BC37314 and BC37340 correspond to the C# CS8926 and CS8927, respectively.

## Related Items
[related]: #related-items

- [Static abstract members in interfaces](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-11.0/static-abstracts-in-interfaces.md) — the C# 11 proposal for static abstract interface members
- [RuntimeFeature.VirtualStaticsInInterfaces](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.runtimefeature.virtualstaticsininterfaces) — the runtime capability member that gates static abstract interface member consumption

[static-abstracts]: https://github.com/dotnet/csharplang/blob/main/proposals/csharp-11.0/static-abstracts-in-interfaces.md
[runtime-feature]: https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.runtimefeature.virtualstaticsininterfaces
