# Consuming C# Extension Members

* [x] Proposed
* [x] Prototype: Complete
* [x] Implementation: Complete
* [x] Specification: [Complete](spec-consume-csharp-extension-members.md)

## Summary
[summary]: #summary

This specification defines how the Visual Basic language consumes C# 14 extension members — extension properties and extension operators declared in C# extension blocks, in addition to the extension methods the language already consumes. An extension member is recognized from metadata by the shape of its containing type and by its marker attribute, is reduced against the receiver type, and participates in member access (`obj.Property`), assignment (`obj.Property = value`), and operator resolution (`a + b`).

The specification covers consumption only. Visual Basic has no declaration syntax for these members: an extension property or operator is not declared by the language. The boundary is drawn explicitly and is not changed by this specification.

The C# equivalent is specified in the [extensions][extensions] proposal for C# 14. This specification describes the Visual Basic side of the same metadata contract. The consumption of static abstract interface members, the other member family on which .NET generic math relies, is the subject of a separate specification.

## Motivation
[motivation]: #motivation

Modern .NET libraries increasingly ship extension members that Visual Basic could not previously consume through their natural syntax. C# 14 allows an extension block to declare properties and operators as well as methods. Extension methods are available to Visual Basic today: a C# `[Extension]` method is consumed as `receiver.Method()`. Extension properties and operators are not: `"hello".CharCount` reports that `CharCount` is not a member of `String`, and `a + b` does not resolve to the extension operator. The only way to call them is to invoke the underlying accessor or operator method directly, which is verbose and diverges from the API's documented shape. Under `Option Strict Off`, a failed extension-operator lookup degrades to late binding and fails at run time, which is worse than a compile-time error.

Because the interactive window and ordinary compilation share a single implementation of the language, the consumption rules must be identical in both modes.

## Detailed design
[design]: #detailed-design

The feature introduces no new syntax. It extends the mechanism the language already uses — extension-member fallback lookup — to the property and operator member kinds.

Throughout this section, the examples reference a C# library `ExternLib`. It declares an `[Extension]` class with an extension block over `String` (an `Int32` property `CharCount` and a method `Shout`), a classic extension method `Twice` over `Int32`, an extension block over a value type `Vec` with an `operator +`, and a generic extension block whose receiver is the block's type parameter.

### Representation in metadata

A C# 14 extension block is emitted into metadata in a fixed shape:

- An `[Extension]` static class at namespace scope, the **extension container**.
- A nested **grouping type** named `<G>$<content-hash>`, itself marked `[Extension]`, that holds the extension-member implementations.
- A nested **marker type** named `<M>$<content-hash>`: a `SpecialName`, `static`, public class deriving from `System.Object`, arity zero, with no interfaces, and with a single static `SpecialName` method named `<Extension>$(receiver)` whose parameter type is the receiver type of the extension block.
- The extension members themselves — methods, properties, and operators — as members of the grouping type, each marked with `System.Runtime.CompilerServices.ExtensionMarkerAttribute`. The single string argument of the attribute is the metadata name of the member's marker type, which ties the member to its extension block.

Extension methods exist in two metadata shapes. The classic shape is a top-level `[Extension]` static method with the receiver as its first parameter; the language has always consumed that shape. A C# 14 extension method in an extension block uses the grouping-type shape. Extension properties and operators appear only in the grouping-type shape, so the classic extension-method machinery, which recognizes only `[Extension]` methods, never sees them.

### Recognition

The compiler recognizes the following from metadata:

- An **extension grouping type** is a nested type inside an `[Extension]` container that is itself marked `[Extension]`.
- An **extension marker type** is a nested type that is `SpecialName`, `static`, public, arity zero, derives from `System.Object`, has no interfaces, and has a single `<Extension>$(receiver)` marker method.
- An **extension member** is a method or property inside an extension grouping type that carries `ExtensionMarkerAttribute`.

The **receiver type** of a grouping type is the parameter type of the marker method in its nested marker type. For a non-generic extension block the receiver type is concrete; for a generic extension block it is the grouping type's own type parameter.

```vbnet
Imports ExternLib

Dim count As Integer = "hello".CharCount      ' Okay: extension property getter; count = 5
Dim loud As String = "hi".Shout()             ' Okay: C# 14 extension method
Dim twice As Integer = 5.Twice()              ' Okay: classic extension method
Dim sum As Vec = New Vec(1, 2) + New Vec(10, 20)   ' Okay: extension operator; sum = (11, 22)
```

### Collection and lookup scope

Extension members are collected with the same scope rule as extension methods. The binder walks its chain of scopes, and a namespace that is in scope through an `Imports` clause contributes the extension members of the `[Extension]` containers it declares. An extension member therefore participates in lookup only when its containing namespace is in scope at the use site. When it is not, a member access reports the ordinary error for a name that is not a member of the receiver:

```vbnet
' Without 'Imports ExternLib', the extension members are not in scope.
Dim count As Integer = "hello".CharCount      ' Error BC30456: 'CharCount' is not a member of 'String'
```

This mirrors the existing behavior for extension methods and introduces no new scope rule.

### Reduction

An extension member is reduced against the instance type before it is used. Reduction exposes the receiver and, for the purposes of the binder, makes the member look like an ordinary member of the receiver.

- **Extension property.** The receiver type is taken from the marker method. The property is reduced to a property whose `GetMethod` and `SetMethod` are reduced accessors and whose `ReceiverType` is the receiver type. A reduced property is presented without its receiver parameter, so `obj.Property` and `obj.Property = value` bind through the ordinary member-access and assignment paths. Because a reduced property is an ordinary member of the receiver, it participates wherever ordinary member access participates: a chained access such as `obj.Inner.Value` binds each segment through the same lookup, and a `With` statement over the receiver binds its `.<member>` accesses through the same path. The setter is symmetric with the getter.
- **Extension operator.** The receiver is the operator's first (left-operand) parameter. The operator keeps all of its operands and is presented with `MethodKind.UserDefinedOperator`, so it flows through the existing operator-resolution machinery. The grouping type's operator is emitted by C# as a throwing stub; the real implementation is the top-level non-extension method with the same signature on the extension container, and that method is preferred as the call target.
- **Extension method.** The corresponding top-level `[Extension]` method — the "shim" that takes the receiver as an explicit parameter — is located and reduced through the classic extension-method machinery. The existing path is unchanged.

The reduction of an extension property or operator is expressed entirely in terms of the metadata shape; no new syntax is involved.

### Generic extension blocks

When the receiver type contains the grouping type's own type parameters, the receiver type must be inferred from the instance type before the member can be used. The reduction infers the type parameters referenced by the receiver, constructs the grouping type with the inferred arguments, verifies constraints, and reduces the member on the constructed type. This is the same inference machinery the classic extension-method reduction uses, applied to the receiver-as-parameter shape of a marker method.

```vbnet
Imports ExternLib

Dim arr As Integer() = New Integer() {1, 2, 3}
Dim n As Integer = arr.TotalCount             ' Okay: receiver T() is inferred with T = Integer
```

### Name resolution and priority

Extension members are a fallback. Member lookup resolves instance members first; when an instance member of the requested name binds successfully, no extension member is consulted. When lookup does not find an instance member, the extension members that are in scope are reduced and considered.

```vbnet
Imports ExternLib

' ExternLib declares: extension(string s) { public int Length => s.Length * 1000; }
Dim n As Integer = "hello".Length             ' Okay: binds to String.Length; the extension property is not consulted
```

Among the extension candidates, the candidate from the nearest scope wins, and when two candidates are equally good the first collected wins. No ambiguity diagnostic is produced. This is the same merge rule that applies to the existing extension-method fallback.

**Decision**: Extension members are never promoted above instance members. Promoting them would change the meaning of existing programs that declare an instance member shadowed by an in-scope extension member; a pure fallback preserves the meaning of every existing program.

### Strict modes and late binding

Extension members participate only in early-bound lookup. Two consequences follow:

- **`Object` receivers.** An `Object` receiver never participates in extension-member lookup. Under `Option Strict Off`, a member access on an `Object` receiver remains late-bound and is resolved at run time; the extension member is not considered. This matches the existing behavior for extension methods.
- **Extension operators.** Extension operators are resolved at compile time under both `Option Strict On` and `Option Strict Off`. Under `Option Strict Off`, an operator that previously degraded to late binding and failed at run time now resolves to the extension operator when one is in scope. A compile-time resolution is strictly better than a silent run-time failure.

## Drawbacks
[drawbacks]: #drawbacks

- **Behavior change under `Option Strict Off`.** An operator expression that previously compiled through late binding — and failed at run time — now resolves to an extension operator at compile time when one is in scope. This is a correction, but a program that relied on the late-bound path changes meaning. The new resolution happens only when a matching operator is in scope, so the change is limited to programs that name such an operator.

- **Metadata complexity.** Extension members are recognized from a three-part metadata shape — container, grouping type, marker type — rather than from a single attribute on a method. The recognition is more elaborate than the classic extension-method recognition, and the reduction of generic extension members reuses the type-inference machinery, which is the most intricate part of extension-method handling.

## Alternatives
[alternatives]: #alternatives

- **Recognize extension properties and operators by their accessor or `op_` names.** A compiler could treat a static method named `get_*`, `set_*`, or `op_*` as an extension member without reading the grouping-type and marker-type shape. This was rejected: the top-level implementation methods of an extension block carry no distinguishing attribute, so a hand-written static method with such a name would be misidentified. The metadata shape is the only reliable signal.

- **Require an `Imports` of the extension container's namespace to be explicit and absolute.** The scope rule could have been tightened, but the language already resolves extension methods through the binder chain, and extension members use the same rule. No new scope mechanism is warranted.

- **Expose a syntax for declaring extension properties and operators.** Declaring extension members in Visual Basic source is a separate, larger feature that would add syntax to the language. The consumption-side value stands alone, and the declaration side is deliberately out of scope.

## Unresolved questions
[unresolved]: #unresolved-questions

None.

## Considerations
[considerations]: #considerations

### Runtime support

No runtime support is required for extension members: an extension property, operator, or method is compiled to an ordinary static call. The reduction is entirely a binding-time transformation.

### API versioning

Because Visual Basic only consumes these members, the versioning surface is entirely on the C# side. A library that adds an extension property or an extension operator changes which Visual Basic programs compile against it: a use that previously reported an error now binds. A library that removes one changes a previously valid use into an error. Neither direction affects the meaning of a program that does not name the member.

### Alignment with C#

The reduction and fallback semantics are aligned with C#: extension members are a fallback below instance members, they are collected only from namespaces in scope, and a reduced extension member is presented to the binder as an ordinary member of the receiver. The instance-member priority is the C# rule, preserved unchanged.

## Related Items
[related]: #related-items

- [Extensions](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-14.0/extensions.md) — the C# 14 proposal that introduces extension members, including the metadata encoding this specification consumes

[extensions]: https://github.com/dotnet/csharplang/blob/main/proposals/csharp-14.0/extensions.md
