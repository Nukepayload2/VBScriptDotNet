# ByRef-Like Type Safety

* [x] Proposed
* [x] Prototype: Complete
* [x] Implementation: Complete
* [x] Specification: [Complete](spec-byref-like-safety.md)

## Summary
[summary]: #summary

This specification defines how the Visual Basic language recognizes **byref-like** types — value types that must be confined to the execution stack, such as `Span(Of T)` and `ReadOnlySpan(Of T)` — and how it enforces the safety rules that keep their values off the managed heap. It also defines how the language consumes the C# `allows ref struct` anti-constraint: byref-like types are accepted as type arguments for generic parameters that carry the anti-constraint, the capability is propagated onto Visual Basic source type parameters that override or implement the declaring members, and those type parameters are validated and used as though they were byref-like.

The specification covers two capabilities:

1. **Byref-like type recognition and restricted-type enforcement.** A type is byref-like when its definition carries `System.Runtime.CompilerServices.IsByRefLikeAttribute`. Byref-like types are restricted types for the purpose of the existing restricted-type analysis, and the compiler rejects every use that would move a byref-like value onto the managed heap. The `Obsolete` marker that byref-like types carry in metadata for the benefit of older compilers is suppressed.

2. **Consumption of the `allows ref struct` anti-constraint.** When a generic parameter in referenced metadata carries `System.Reflection.GenericParameterAttributes.AllowByRefLike` (0x0020), the language allows byref-like types as type arguments for that parameter, propagates the capability to Visual Basic source type parameters that override or implement the declaring members, and applies the byref-like rules to those type parameters throughout the bodies of the overriding or implementing members.

The C# equivalent of the second capability is specified in the [Ref Struct Interfaces][ref-struct-interfaces] proposal; this specification describes the Visual Basic side of the same metadata contract.

## Motivation
[motivation]: #motivation

Modern .NET relies on stack-only value types for high-performance APIs. `Span(Of T)`, `ReadOnlySpan(Of T)`, `Utf8String`, and many other types are declared as C# `ref struct` types and must never appear on the managed heap. C# 13 additionally allows such types to participate in generic abstractions through the `allows ref struct` anti-constraint, so that types like `System.Collections.Generic.IEnumerable(Of T)` and user-defined interfaces can be instantiated with byref-like arguments.

Visual Basic has no declaration syntax for byref-like types. Its role in the ecosystem is to *consume* them: to use `Span(Of T)` in method bodies, to pass byref-like values by value, and to implement the generic interfaces that accept them. Historically the compiler did not recognize byref-like types:

- `ITypeSymbol.IsRefLikeType` was hard-coded to `False`.
- The restricted-type analysis recognized only the three special types `TypedReference`, `ArgIterator`, and `RuntimeArgumentHandle`.
- The compiler did not suppress the metadata `Obsolete` marker carried by byref-like types.

The consequences were twofold. Code that used a byref-like type directly was rejected with an obsolete error, even though the use was otherwise safe. Code that bypassed that error — or that used a byref-like type in a context the three-special-type analysis did not cover — compiled and produced invalid IL that failed at run time, typically with `InvalidProgramException`. Converting these misuses into compile-time errors is a correction, not a regression: no program that compiled and behaved correctly changes meaning.

Because the interactive window and ordinary compilation share a single implementation of the language, the rules must be identical in both modes. The same compiler accepts `.vb` projects and script submissions; the safety rules must not diverge between them.

## Detailed design
[design]: #detailed-design

### Byref-like type recognition

#### `IsRefLikeType` and `IsRestrictedType`

A type is a **byref-like type** if and only if its definition carries `System.Runtime.CompilerServices.IsByRefLikeAttribute`. Byref-like types are declared in other languages — in practice, as C# `ref struct` types; Visual Basic never produces a byref-like type from source. The language therefore does not introduce a declaration modifier for byref-like types, nor does it introduce `scoped` or `UnscopedRef` annotations. Byref-like escape and lifetime behavior is expressed entirely by the compiler's internal rules.

The symbol property `IsRefLikeType` is defined to read the presence of `IsByRefLikeAttribute`; it is no longer hard-coded to `False`. The `IsRestrictedType()` predicate, which previously recognized only the three special restricted types `TypedReference`, `ArgIterator`, and `RuntimeArgumentHandle`, is extended so that a type is restricted if and only if it is byref-like or is one of those three special types. Because the existing restricted-type checkpoints — fields, arrays, conversions, lambdas, anonymous types, and so on — are all expressed in terms of this predicate, the extension applies them to every byref-like type without introducing new checkpoint sites.

Note that the legacy special types remain restricted even though they are not byref-like. `TypedReference`, `ArgIterator`, and `RuntimeArgumentHandle` are value types whose managed representation is not safe to box, so the existing restrictions on them are preserved unchanged.

#### Suppressing the byref-like obsolete marker

Byref-like types carry an `Obsolete` attribute in metadata whose purpose is to prevent compilers that do not understand the stack-only rules from using them; [span-safety] describes this convention. A compiler that understands byref-like types ignores this particular form of `Obsolete`. The compiler therefore filters the metadata `Obsolete` attribute on a type when the type is byref-like and carries no other obsolete data. This makes `Span(Of Integer)` directly usable and bindable in Visual Basic source:

```vbnet
Imports System

Dim buffer As New Span(Of Integer)(New Integer(9) {})
buffer(0) = 42                              ' Okay: element access is by reference, no boxing
```

#### Restricted-type checks

The following checkpoints reject every use of a byref-like type that would move its value onto the managed heap or otherwise violate the stack-only invariant.

| Error code | Diagnostic | Condition |
|---|---|---|
| BC31393 | `ERR_RestrictedAccess` | Invoking an instance member inherited from `Object` or `ValueType` on a byref-like receiver, which would box the receiver |
| BC31394 | `ERR_RestrictedConversion1` | A conversion to `Object` or `ValueType`, which boxes the value |
| BC31396 | `ERR_RestrictedType1` | A use as a `Nullable(Of T)` type argument, a field, an array element, an array return value, a `ByRef` parameter, an anonymous type, a delegate or conversion target, or a generic type argument whose parameter does not carry the `allows ref struct` anti-constraint |
| BC32061 | `ERR_ConstraintIsRestrictedType1` | A restricted or special type used as a type constraint |
| BC36598 | `ERR_CannotLiftRestrictedTypeQuery` | A LINQ query that would capture or box a byref-like value |
| BC36640 | `ERR_CannotLiftRestrictedTypeLambda` | A lambda that would capture a byref-like value in its closure |
| BC37052 | `ERR_CannotLiftRestrictedTypeResumable1` | An async or iterator method whose state machine would capture a byref-like value |

The field, array-element, and boxing restrictions align with the C# [span-safety] rules: a byref-like type cannot be a field of any type except another byref-like type, cannot be an array element, and cannot be converted to a non-byref-like type.

```vbnet
Class C
    Private _field As Span(Of Integer)     ' Error BC31396: cannot be a field
    Private _arr() As Span(Of Integer)     ' Error BC31396: cannot be an array element

    Function GetSpan() As Span(Of Integer) ' Okay: a byref-like value may be returned by value
    End Function

    Sub Pass(ByRef s As Span(Of Integer))  ' Error BC31396: cannot be a ByRef parameter
    End Sub
End Class

Async Function ProcessAsync(s As Span(Of Integer)) As Task   ' Error BC36932: cannot be an async parameter
    Await Task.Delay(1)
End Function

Sub Use(s As Span(Of Integer))
    Dim f = Function() s.Length            ' Error BC36640: a lambda cannot capture a byref-like value
End Sub
```

Note that a scalar by-value return of a byref-like type is legal; the restriction applies to array returns and to `ByRef` parameters. Async and iterator parameters are rejected at the declaration (BC36932), consistently with the handling of concrete byref-like parameters.

#### Display

The IDE displays the modifier `ByRef Like Structure` for byref-like types — for example, `ByRef Like Structure Span(Of T)`. The modifier is presentation-only. It is not part of the language syntax and cannot be written in source code, just as a `ByRef Function` is a display convention rather than a declaration form.

### Script and REPL constraints

Submissions in the interactive window are compiled as script classes. A top-level declaration becomes a field of the script class; the trailing expression of a submission is converted to the submission return type `Object`; and the script initializer method is always an async method. Each of these interacts with the byref-like rules to produce three constraints that are specific to the interactive window:

- **Top-level variables.** A top-level declaration of a byref-like type is an error (BC31396), because the declaration would become a field of the script class and byref-like types cannot be fields. This is the same rule, anchored to the same semantics, that C# applies to top-level variables of byref-like type in its scripting host.

- **Submission results.** When the result of a submission — the trailing expression, or the value printed by the `?` command — has a byref-like type, the implicit conversion to the submission return type `Object` boxes the value and is therefore an error (BC31394). No special printing behavior is provided: the value is bound to the stack and cannot be persisted, so printing the name of a type that cannot be referenced has no interactive value.

- **Await across submissions.** Because the script initializer method is always async, a top-level `Await` that leaves a byref-like value in scope is captured by the async state machine and is an error (BC37052).

The following uses remain available in the interactive window: byref-like locals inside method bodies, `ByVal` value parameters, and consumption of members whose generic parameters carry the `allows ref struct` anti-constraint. Diagnostics are not parameterized by mode: the interactive window reports the same restricted-type errors as ordinary compilation, so no new diagnostic text is required for the script mode.

### Consumption of the `allows ref struct` anti-constraint

#### Representation in metadata

C# 13 allows a generic parameter to accept byref-like type arguments by declaring `where T : allows ref struct`. The capability is encoded in metadata as `System.Reflection.GenericParameterAttributes.AllowByRefLike` (0x0020). Whether the target runtime supports the feature can be determined by checking for the presence of the `System.Runtime.CompilerServices.RuntimeFeature.ByRefLikeGenerics` field, which is available on .NET 8 and later ([byref-like-generics]). The feature does not depend on `RefSafetyRulesAttribute`, which is a separate mechanism for versioning ref-safety rules.

#### Type arguments

The language allows a byref-like type as a type argument when the corresponding generic parameter carries the `allows ref struct` anti-constraint. When the parameter does not carry the anti-constraint, the existing restriction applies and the use is rejected with BC31396. The three legacy restricted types are always rejected, whether or not the parameter carries the anti-constraint.

```vbnet
Public Interface IThing(Of T)              ' In C#: public interface IThing<T> where T : allows ref struct
End Interface

Public Class C(Of T)
End Class

Dim x As IThing(Of Span(Of Integer))       ' Okay: the parameter allows byref-like type arguments
Dim y As C(Of Span(Of Integer))            ' Error BC31396: the parameter has no such capability
```

#### Override and interface implementation

Visual Basic has no syntax for declaring the `allows ref struct` anti-constraint. The only source of the capability for a Visual Basic source type parameter is the metadata of the member it overrides or implements. The compiler propagates the capability in both directions:

- **Method type parameters.** The `AllowsRefLikeType` property of a type parameter of a method is no longer always `False`. When the method overrides a base method, the property is taken from the corresponding type parameter of the overridden method. When the method explicitly implements an interface member, the property is taken from the corresponding type parameter of that interface member. The property is computed lazily and is guarded against reentrancy while interface implementations are being resolved.

- **Type type parameters.** For a type parameter of a generic type, the capability is computed from the transitive interface set of the type: an ordinal is capable when some interface `I(Of ...)` in that set has a parameter at the ordinal carrying the anti-constraint and the corresponding type argument is the container's own type parameter. The computation is lazy and cached. This mechanism is specific to Visual Basic: C# obtains the same capability from the explicit `allows ref struct` syntax on the type declaration, which Visual Basic does not have.

After propagation, overriding or implementing a member whose generic parameters carry the anti-constraint compiles successfully; the constraint-mismatch errors that would otherwise be reported are no longer produced, and the source type parameter reports the capability. Throughout the body of the overriding or implementing member, the type parameter is validated and used as though it were byref-like.

```vbnet
Public Interface IThing(Of T)              ' In C#: public interface IThing<T> where T : allows ref struct
    Sub Use(value As T)
End Interface

Public Class Impl(Of T)
    Implements IThing(Of T)

    Public Sub Use(value As T) Implements IThing(Of T).Use
    End Sub
End Class
```

When a type parameter participates in the implementation of several interfaces, the capability is the union over the interfaces: an ordinal is capable if any interface in the transitive set makes it capable. If a member must simultaneously implement two interface members whose corresponding parameters disagree, the existing constraint-consistency check reports the mismatch.

#### Method-body rules

The rules that apply to concrete byref-like values are applied to a type parameter that carries the capability. A new predicate, `IsRefLikeOrAllowsRefLikeType()`, is defined as:

```vbnet
IsRefLikeType OrElse SpecialType.IsRestrictedType() OrElse (TypeParameter AndAlso AllowsRefLikeType)
```

For types that are not type parameters the predicate is exactly `IsRestrictedType()`, so it is a strict superset of the legacy predicate and does not change the behavior of existing code. The predicate drives the field, array, `ByRef`, async-capture, lambda, anonymous-type, delegate, property-type, return-array, and array-literal checkpoints. A conversion from a capable type parameter to `Object` is rejected — the conversion classifier returns no conversion — and the general conversion error is reported at the use site.

```vbnet
Public Class Impl(Of T)
    Implements IThing(Of T)

    Private _field As T                    ' Error BC31396: cannot be a field
    Private _arr() As T                    ' Error BC31396: cannot be an array element

    Public Sub Use(value As T) Implements IThing(Of T).Use
        Dim local As T = value             ' Okay: stack local
        Dim boxed As Object = value        ' Error BC30311: a capable type parameter cannot be boxed
    End Sub
End Class
```

Note that a type parameter that carries the capability is subject to these restrictions for every substitution, including substitutions with non-byref-like types. `Impl(Of Integer)` is therefore also subject to the field restriction on `T`. This matches the C# semantics of the anti-constraint: a type parameter declared `allows ref struct` cannot be used in contexts that a byref-like substitution would violate, regardless of the actual type argument.

#### The BC31393 checkpoint

Two vectors can box a byref-like receiver. Both are rejected with BC31393 (`ERR_RestrictedAccess`), whose message states that the type is a restricted type and cannot be used to access members inherited from `Object` or `ValueType`:

- **Direct invocation.** Invoking on a byref-like receiver an instance member that is inherited from `Object` or `ValueType` and not overridden by the receiver — `GetHashCode`, `ToString`, `GetType`, or `Equals` — is an implicit boxing operation and is rejected. The check applies to byref-like-capable type parameters as well as to concrete byref-like types. An override is unaffected: when the receiver overrides the member, the member binds to the receiver's own type and the check does not apply. Such a member may still be obsolete, as `Span(Of T).GetHashCode` is.

- **AddressOf and delegate creation.** Creating a delegate from an instance method of a byref-like receiver requires boxing the receiver into the delegate target, because the delegate must carry a managed reference to the receiver. This is rejected with BC31393 for every instance member of a byref-like receiver — inherited members and members declared on the receiver alike. Without the check, the emitted code would box the receiver and throw `InvalidProgramException` at run time. Shared methods have no receiver and are not affected. Receivers that are not byref-like are not affected: for an ordinary value type, boxing the receiver into the delegate target is legal.

```vbnet
Dim r As New R()
r.GetHashCode()                            ' Error BC31393: member inherited from Object
Dim d As Func(Of String) = AddressOf r.M   ' Error BC31393: delegate creation must box the receiver
r.Length                                   ' Okay: own member, no boxing
```

The two vectors are mutually exclusive — a direct invocation is bound through the invocation binder and an `AddressOf` expression is bound through the delegate-creation path — so a single use reports exactly one diagnostic.

#### Code generation

Code generation requires no change. A call through a generic receiver is always emitted as a `constrained.` callvirt, so invoking an interface member through a capable type parameter never boxes the receiver. This is the existing behavior of the emitter.

```vbnet
Public Interface IThing(Of T)              ' In C#: public interface IThing<T> where T : allows ref struct
    Sub Use(value As T)
End Interface

Public Sub Invoke(Of T As IThing(Of T))(value As T)
    value.Use()                            ' Okay: emitted as constrained. callvirt
End Sub
```

#### Boundaries and limitations

The following uses of a byref-like type and of a capable type parameter are available: `ByVal` scalar parameters, by-value returns, stack locals, and constrained calls through generic receivers. The following uses are rejected: `ByRef` parameters, fields, array elements and array parameters, boxing conversions, async and iterator parameters (BC36932) and captures (BC37052), lambda captures (BC36640), anonymous-type fields, and property types.

The following are inherent Visual Basic limitations and are independent of this feature:

- **Implicit interface implementation.** Visual Basic supports interface implementation only through an explicit `Implements` clause. A method that matches an interface member by name but has no `Implements` clause does not satisfy the interface (BC30149). This applies to every interface member, not specifically to members whose parameters carry the anti-constraint, and is not a limitation introduced by this feature.

- **Default interface methods.** The compiler that implements this specification does not support default interface methods. The scenario in which a default interface member is invoked through a capable type parameter is therefore unreachable.

- **Byref enumerators.** Visual Basic has no declaration syntax for properties that return by reference, but it consumes them from metadata. A `Current` property that returns a value by reference through a `ref readonly` signature (metadata `modreq(In)`) is imported and read through the compiler's automatic dereference, so the standard byref-like enumerators — for example, `ReadOnlySpan(Of T).Enumerator.Current` — can be used with a `For Each` statement. A `Current` that returns a mutable reference (plain `ref`, without `modreq(In)`) was already readable and remains so. Visual Basic still cannot author such an enumerator in source (there is no declaration syntax for a member that returns by reference), and a `ref readonly` `Current` is read-only: the compiler rejects assignments through it, consistently with the other write rules for ref-readonly values.

- **Escape analysis.** The C# `scoped` and ref-escape analysis is a C#-specific mechanism and is not part of this specification.

## Soundness
[soundness]: #soundness

The invariant that motivates every rule in this specification is that a value of a byref-like type must never appear on the managed heap. Each restricted-type check is a compile-time refusal of an operation that would place a byref-like value on the heap:

1. Fields of classes and static fields cannot have a byref-like type, because the field storage would live on the heap.
2. Array elements cannot have a byref-like type, because the element storage would live on the heap.
3. A byref-like value cannot be converted to `Object` or `ValueType`, because boxing allocates on the heap.
4. A byref-like type cannot be used as a type argument unless the corresponding parameter carries the `allows ref struct` anti-constraint, because the substitution could otherwise embed the value in a heap-allocated representation.
5. A byref-like value cannot be captured by a lambda closure, an anonymous type, a LINQ query closure, or an async or iterator state machine, because each of these lifts the value into a heap-allocated frame.
6. A byref-like type cannot be a `ByRef` parameter or the element type of an array return, because these would permit the value to escape its stack frame.

The `allows ref struct` anti-constraint generalizes rule (4) without weakening the invariant. A type parameter that carries the anti-constraint is treated as potentially byref-like: every rule that applies to a concrete byref-like value applies to a value of such a type parameter, and a byref-like type may be substituted for it. The relaxation is confined to rule (4), which is sound because the substituted type is subject to the full byref-like rule set throughout the body of the generic member. No use of the type parameter can move the value onto the heap, because the compiler checks every use of the parameter as though the parameter were byref-like.

The converse property — that no previously valid program becomes invalid — holds as follows. For every existing program, either the relevant type is an ordinary type parameter, for which the capability is `False` and the new predicate agrees with the legacy predicate; or it is a concrete type, for which the new predicate agrees with the legacy predicate unless the type is byref-like. A concrete byref-like type in a now-rejected context produced invalid IL at run time before this feature, so the new compile-time errors are corrections rather than regressions of valid behavior. Programs that did not use byref-like types observe no change in behavior.

## Drawbacks
[drawbacks]: #drawbacks

- **Behavior change.** Programs that used byref-like types in now-rejected contexts compiled before this feature and failed at run time. They now fail at compile time instead. This is a correction, but projects that depended on the (broken) runtime behavior must migrate. The metadata `Obsolete` suppression also changes which programs the compiler accepts: code that was rejected as obsolete is now accepted and subject to the restricted-type checks.

- **Model difference from C#.** Visual Basic does not declare byref-like types and does not expose `scoped` or `UnscopedRef`. Byref-like escape and lifetime behavior is expressed entirely by compiler-internal rules. This is sufficient for consumers — the information carried by C# signatures is honored — but Visual Basic cannot author the new source-level contracts, such as "this parameter does not escape," that C# expresses with annotations.

- **Over-restriction for concrete substitutions.** A capable type parameter is restricted for every substitution, including non-byref-like ones. `Impl(Of Integer)` is therefore subject to the field restriction on `T` even though `Integer` is not byref-like. This matches C# and is required for the anti-constraint to be sound, but it is more conservative than a per-instantiation rule would be.

## Alternatives
[alternatives]: #alternatives

- **Keep the analyzer external.** The byref-like checks could remain in a standalone analyzer referenced by projects. This leaves the interactive window unprotected, leaves the compiler without internal enforcement, and does not suppress the metadata obsolete marker. Boxed byref-like values would continue to produce invalid IL whenever the analyzer was not referenced.

- **Restrict only the interactive window.** The REPL-specific constraints could be added without extending the restricted-type analysis to ordinary compilation. This splits semantics between two modes that share a single compiler and abandons the interop value of the feature, which lives in ordinary method bodies that consume `allows ref struct` members.

- **Port the full C# ref-safety model.** The language could adopt `scoped`, `UnscopedRef`, and a declaration syntax for byref-like types. This exceeds the positioning of the language: Visual Basic does not declare byref-like types, so `scoped` would have nothing to annotate, and there is no authoring scenario for it. The cost is high and the benefit is limited to scenarios Visual Basic does not have.

- **Auto-apply the anti-constraint.** The language could automatically treat every unconstrained generic parameter as capable, analogously to a C# proposal to auto-apply `allows ref struct`. This was rejected for the same reasons given there: it makes code compile on one target framework and fail on another with no syntactic indicator, and subtle changes to an interface (such as adding a default member) would change the behavior of existing instantiations at a distance.

## Unresolved questions
[unresolved]: #unresolved-questions

None.

## Considerations
[considerations]: #considerations

### Runtime support

The feature relies on several pieces of runtime and library support:

- `System.Runtime.CompilerServices.IsByRefLikeAttribute`, which marks byref-like types and must be recognized by the compiler. It is available in the base class libraries.
- `System.Reflection.GenericParameterAttributes.AllowByRefLike` (0x0020), which encodes the anti-constraint on a generic parameter in metadata.
- `System.Runtime.CompilerServices.RuntimeFeature.ByRefLikeGenerics`, which indicates that the runtime supports byref-like generic arguments. The field is available on .NET 8 and later ([byref-like-generics]); the API was added in [dotnet/runtime#98070][rt-pr-98070].
- Support in `System.Reflection.Metadata` for reading the flag, and in the runtime for the `constrained.` call mechanism, which is already present.

The feature does not require `RefSafetyRulesAttribute`; that attribute versions the ref-safety rules and is unrelated to the anti-constraint.

### API versioning

Adding the `allows ref struct` anti-constraint to an existing generic parameter is not a source-breaking change: it expands the set of types that may be substituted, and every new substitution is checked against the byref-like rules at the point of use. Removing the anti-constraint, by contrast, is always a breaking change, both source and binary: existing substitutions with byref-like types would cease to compile.

API authors should avoid adding the anti-constraint to a parameter of an abstract or virtual member if the member may later acquire an implementation that cannot be written for a byref-like substitution. Adding a default interface method to an interface, for example, affects byref-like implementors in C#; in Visual Basic, which does not support default interface methods, adding any member to an interface requires implementors to provide an explicit implementation.

Because Visual Basic consumes the anti-constraint from metadata and has no syntax for it, the versioning surface is entirely on the C# side: a library that adds or removes the anti-constraint changes which Visual Basic programs compile against it.

## Testing
[testing]: #testing

The feature is exercised by in-memory compilation tests that have no side effects: they do not perform network access, file writes, process launches, or registry access.

- **Semantic tests.** A matrix of positive and negative cases exercises the restricted-type rules for concrete byref-like types and for capable type parameters: fields, array elements and array parameters, `ByRef` parameters, boxing conversions, async and iterator methods and parameters, lambda captures, anonymous types, property types, array literals, and constrained interface calls. The matrix includes regression anchors for the legacy restricted types (`TypedReference`, `ArgIterator`, `RuntimeArgumentHandle`) and for ordinary type parameters, confirming that the extended predicate does not change behavior for non-byref-like code. Emitted IL is verified for the constrained-call path and for the rejected delegate-creation path.

- **Symbol tests.** Tests for the propagation of the capability: overriding a C# generic method whose parameter carries the anti-constraint compiles and the source type parameter reports the capability; explicitly implementing such an interface member compiles and reports the capability; and the embedding and runtime-capability cases are covered.

- **Script tests.** Tests for the interactive window verify the three top-level constraints — the top-level variable, the submission result, and `Await` across submissions — and the availability of method-body locals and `ByVal` parameters.

- **External coverage.** The scenario inventory of the standalone Visual Basic byref-like analyzer and the consumption points of the C# [RefStructInterfacesTests][ref-struct-interfaces-tests] are mapped point-by-point onto the local cases. Divergences are documented rather than hidden: a `Using` statement over a byref-like disposable reports the pattern-based error rather than a boxing error; a scalar by-value return of a byref-like type is legal; and the C#-specific scenarios — default interface methods and escape analysis — are marked as not applicable with the reasons stated above. For Each over a byref-like enumerator is exercised rather than marked not applicable: its `Current` is a ref readonly property, which is consumed as described under Byref enumerators.

## Related Items
[related]: #related-items

- [Ref Struct Interfaces](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-13.0/ref-struct-interfaces.md)
- [Span safety](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-7.2/span-safety.md)
- [Byref-like generics design](https://github.com/dotnet/runtime/blob/main/docs/design/features/byreflike-generics.md)
- [Restricted types in the Visual Basic specification](https://github.com/dotnet/vblang/blob/main/spec/types.md)
- [RefStructInterfacesTests](https://github.com/dotnet/roslyn/blob/main/src/Compilers/CSharp/Test/Symbol/SymbolTests/RefStructInterfacesTests.cs)

[ref-struct-interfaces]: https://github.com/dotnet/csharplang/blob/main/proposals/csharp-13.0/ref-struct-interfaces.md
[span-safety]: https://github.com/dotnet/csharplang/blob/main/proposals/csharp-7.2/span-safety.md
[byref-like-generics]: https://github.com/dotnet/runtime/blob/main/docs/design/features/byreflike-generics.md
[vblang-types]: https://github.com/dotnet/vblang/blob/main/spec/types.md
[ref-struct-interfaces-tests]: https://github.com/dotnet/roslyn/blob/main/src/Compilers/CSharp/Test/Symbol/SymbolTests/RefStructInterfacesTests.cs
[rt-pr-98070]: https://github.com/dotnet/runtime/pull/98070
