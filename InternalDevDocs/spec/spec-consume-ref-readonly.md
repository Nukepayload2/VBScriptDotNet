# Readonly ByRef Returns

* [x] Proposed
* [x] Prototype: Complete
* [x] Implementation: Complete
* [x] Specification: [Complete](spec-consume-ref-readonly.md)

## Summary
[summary]: #summary

This specification defines how the Visual Basic language **consumes** members that return a value by a *readonly* reference — C# `ref readonly` returns. When a referenced member such as `ReadOnlySpan(Of T).Item` or `ReadOnlySpan(Of T).GetPinnableReference()` returns a value through a `ref readonly` signature, Visual Basic binds the result as a value of the element type, reads it through the compiler's existing automatic dereference, and enforces a read-only write discipline: a write that would store through the returned reference is either refused at compile time or lowered to a copy, so read-only memory is never written through.

The feature introduces no declaration syntax. Visual Basic cannot declare a member that returns by reference, mutable or read-only; it only consumes such members from metadata. The read-only discipline is anchored on the metadata signature of the imported member — a required `modreq([In])` custom modifier on the by-reference return — and is carried on the symbol as a read-only flag that is exposed through the semantic model.

## Motivation
[motivation]: #motivation

Modern .NET uses `ref readonly` returns to expose high-performance, read-only views of data without copying. The canonical example is `ReadOnlySpan(Of T)`:

- `ReadOnlySpan(Of T).Item` — the indexer — returns `ref readonly T`.
- `ReadOnlySpan(Of T).GetPinnableReference()` returns `ref readonly T`.
- `ReadOnlySpan(Of T).Enumerator.Current` returns `ref readonly T`.

The byref-like support specified in ByRef-Like Type Safety already makes `Span(Of T)` and `ReadOnlySpan(Of T)` usable in Visual Basic: construction, `Length`, `Slice`, and other ordinary members bind and run. The indexer is the type's core usage, and it was rejected because the metadata signature of a `ref readonly` return carries a required custom modifier, `modreq([In])`, and the Visual Basic metadata importer rejected every required custom modifier other than `IsExternalInit`.

The gap was observed directly:

```vbnet
Imports System

Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10, 20, 30})
Console.WriteLine(s.Length)                ' Okay: outputs 3
' Console.WriteLine(s(0))                  ' Error BC30643: the property is of an unsupported type
' Dim p As Integer = s.GetPinnableReference() ' Error BC30657: the method has an unsupported return type
```

Members that return by a *mutable* reference — plain `ref`, without `modreq([In])` — were already fully consumable: reading, assigning through, passing as a `ByRef` argument, and inferring from all worked. The gap was precisely the `modreq([In])` form, the same mechanism C# has used to encode `ref readonly` returns since C# 7.2 ([readonly-ref]).

## Detailed design
[design]: #detailed-design

### Representation in metadata

A C# member that returns `ref readonly T` is emitted with a return signature `CMOD_REQD([In]) BYREF T`: the return is by reference and carries a required `System.Runtime.InteropServices.InAttribute` custom modifier *before* the `BYREF` marker. The C# compiler has used this encoding since C# 7.2 ([readonly-ref]). Visual Basic reads the `BYREF` marker as a by-reference return and the element type `T` as the return type; the required `InAttribute` modifier is the marker of read-only-ness.

### Metadata import

Visual Basic's metadata importer historically rejected any member whose signature carries a required custom modifier, with a narrow exception for the `IsExternalInit` modifier used by init-only setters. The `modreq([In])` on a `ref readonly` return fell into that rejection, and a member carrying it was reported as unsupported: a property or indexer as BC30643, a method as BC30657.

This specification adds `System.Runtime.InteropServices.InAttribute` to the set of required modifiers that the importer treats as benign. The exemption is identified by the *attribute identity* of the modifier — the metadata type `System.Runtime.InteropServices.InAttribute` — and not by the mere fact that the modifier is required. No other required modifier is exempted; the importer's rule that a required custom modifier outside the known benign set is unsupported is preserved. In particular, `System.Runtime.InteropServices.OutAttribute` is **not** exempted: C# permits `Out` only on function-pointer parameters, which Visual Basic does not consume.

The exemption applies on two paths:

- **Return path.** A member whose return signature carries `modreq([In])` is imported as a by-reference return rather than rejected.
- **Parameter path.** A parameter whose signature carries `modreq([In])` — the form C# emits for `in` parameters of virtual, abstract, and delegate members — is imported rather than rejected. Ordinary, non-virtual `in` parameters carry only a `[In]` attribute on the parameter row and no signature modifier; the signature-modifier form appears on virtual and delegate signatures, and the exemption makes those members callable from Visual Basic.

**Decision**: exempt required `InAttribute` modifiers uniformly on both the return and parameter paths, rather than on the return path alone. The two paths share a single metadata use-site check; a return-only exemption would require threading a path discriminator through the shared check. Exempting uniformly matches the C# compiler, whose own metadata importer already treats a required `InAttribute` modifier as benign on property signatures, and costs nothing on the parameter path because `In` never signals writability.

A `ref readonly` return is always a by-reference return. If metadata places a required `InAttribute` modifier on a return that is *not* by reference, the signature is inconsistent; such metadata is treated as unsupported, mirroring the C# compiler's consistency contract for read-only by-reference returns.

### Binding type and automatic dereference

When a member returns by reference, Visual Basic binds the expression to the **element type**, not to a by-reference type. The by-ref-ness is carried as a property of the bound expression — the expression is an l-value — and the bound type is the element type. Reading the value is handled by the code generator's existing automatic dereference: when a by-reference-returning call or property access is used as a value, the compiler emits a load-indirect. This mechanism predates this feature and requires no change.

```vbnet
Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10, 20, 30})
Console.WriteLine(s(0))                    ' Okay: reads through the ref readonly return, outputs 10
```

After the import exemption, the indexer binds: the bound expression has type `Integer` and is an l-value whose reference is read-only.

### The read-only flag

The language tracks read-only-ness on the symbol, separately from by-ref-ness. A new property, `ReturnsByRefReadOnly`, is defined on method and property symbols:

- It is `True` when the member returns by reference and the return signature carries a required `InAttribute` modifier.
- It is `False` for every other member, including every member declared in Visual Basic source — there is no declaration syntax for a read-only by-reference return, so no source member can set it.

The flag is exposed through the semantic model: a member whose `ReturnsByRefReadOnly` is `True` reports `RefKind.RefReadOnly` and `ReturnsByRefReadonly = True` through the corresponding symbol APIs. This exposure is the load-bearing surface for analyzers and tooling: they read the true read-only semantics from the symbol model rather than inferring them from displayed text.

**Decision**: expose the read-only flag on the symbol and through the semantic model. The read-only status is a semantic fact about imported metadata — a write through it would corrupt read-only memory — and hiding it from the semantic model would make the feature invisible to analyzers and IDE tooling. Display layering, described below, does not weaken this exposure.

### Write semantics

A `ref readonly` return is a **read-only l-value**: reading through it is legal and dereferences automatically, but the reference behind it must never be written through, because the memory it refers to may genuinely be read-only. The language classifies every write by the kind of receiver, with two outcomes: refuse to compile, or operate on a copy. No write path stores through the read-only reference.

The write discipline is a hard acceptance criterion of this feature: every write path must either be refused with BC30068 or be lowered to a copy, so the value at the read-only location is unchanged after the statement.

#### Direct assignment, compound assignment, and Mid

Assigning directly to a read-only l-value is refused at compile time with **BC30068** (`ERR_LValueRequired`), whose message states that the expression is a value and therefore cannot be the target of an assignment.

```vbnet
Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10, 20, 30})
s(0) = 42                                  ' Error BC30068: cannot assign to a readonly byref return
```

The check is applied to the assignment target before code generation. It covers every assignment form that stores through the target:

- **Direct assignment**, `s(0) = 42`.
- **Compound assignment**, `s(0) += 5`, which expands to a read-modify-write through the target.
- **`Mid` assignment**, `Mid(s(0), 1) = "x"`, whose target is the read-only l-value.

All three route through the same target-adjustment path, so a single check covers the entire store-through-ref surface. The refusal does not apply to compiler-generated write-backs of `ByRef` arguments; those are suppressed for read-only sources as described below.

**Decision**: reuse BC30068 rather than introduce a new error code. BC30068 is the existing Visual Basic diagnostic for a value-shaped target that cannot be assigned to; it is the diagnostic the compiler already produces for the family of read-only assignment targets, and it is consistent with the display convention described below, which presents a read-only l-value to the user as a value. The diagnostic for a `ReadOnly` variable (BC30064, whose message names a `'ReadOnly' variable`) is not used: the target here is a by-reference-returning property or method result, not a `ReadOnly` variable, and that wording would be inaccurate.

Note that C# refuses the same operation with CS8331 ([readonly-ref]); Visual Basic's value-shaped diagnostic and its copy-based handling of `ByRef` arguments (below) are the Visual Basic counterparts of the same rule, expressed in the language's assignment conventions.

#### ByRef arguments

When a read-only l-value is passed as a `ByRef` argument, the compiler **copies** the value into a temporary, passes the temporary by reference, and **discards the write-back**: after the call, the read-only location is unchanged.

```vbnet
Sub M(ByRef v As Integer)
    v = 99
End Sub

Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10, 20, 30})
Dim x As Integer = s(0)                    ' Okay: x = 10
M(s(0))                                    ' Okay: passes a copy; the callee mutates only the copy
Console.WriteLine(s(0))                    ' Okay: still 10; the write-back was discarded
```

This is not a new rule. Visual Basic's copy-out convention writes back to a `ByRef` argument only when the argument can accept the write; passing a literal, a constant, or any other value with no home already compiles and silently discards the callee's writes. A read-only l-value is semantically equivalent to a constant location: **the write-back is discarded, exactly as though the callee were passed a constant by reference.** The callee does execute its assignments, but they mutate the temporary copy, and the read-only memory is never written.

**Decision**: accept the discarded write-back without a warning, consistently with the handling of literals and constants passed by reference. This is a divergence from C#, which reports an error when a `ref readonly` return is used as a `ref` or `out` argument; Visual Basic's copy-out culture prefers a copy to a refusal, and the divergence is confined to the Visual Basic source surface — the metadata contract and cross-language interoperability are unchanged. A future warning when the callee writes its copy remains an option but is not part of this feature.

#### With statements and member chains

A `With` statement captures its receiver so that member accesses evaluate the receiver once. If the receiver is a read-only l-value, the capture is **by value**: storing the reference in a temporary would let a `With`-block write store through into read-only memory. The value capture serves member **reads** — `With o.S(0) : Dim y = .X : End With` reads `.X` through the captured value. A member **write** inside the block is the same member write as a chained assignment whose base receiver is a read-only l-value, and it is refused with BC30068; it does not operate on the captured copy, because silently writing a copy that is then discarded would be indistinguishable from a successful store and would hide the loss of the write.

```vbnet
' Suppose o.S(0) returns ref readonly Row, where Row is a mutable structure with a field X.
With o.S(0)                                ' Captures a copy of the element for reads
    Dim y = .X                             ' Okay: reads the captured value
    .X = 5                                 ' Error BC30068: the receiver o.S(0) is a readonly byref return
End With
```

A member write written directly — without `With` — whose base receiver is a read-only l-value is refused with the same diagnostic, because the read-only reference must never be stored through:

```vbnet
o.S(0).X = 5                               ' Error BC30068: the base o.S(0) is a readonly byref return
```

**Decision**: a member write inside a `With` block whose receiver is a read-only l-value is refused with BC30068, consistent with the chained-assignment form `o.S(0).X = 5`. The value capture is retained solely to serve member reads; it is never a target for a member write.

Member chains used for **reading** are unaffected: `o.S(0).Y` reads through the automatic dereference and yields the field value.

#### Type inference

`Dim x = s(0)` infers the bound type — the element type — and reads through the reference, producing a value copy. The by-ref-ness is stripped by binding (the bound type is the element type), so inference naturally produces a value:

```vbnet
Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10, 20, 30})
Dim x = s(0)                               ' Okay: x is Integer, a copy of s(0)
x = 42                                     ' Okay: x is a local variable
```

### Display

The `ReadOnly` marker for a read-only by-ref return is synthesized **only** in pure debug and internal diagnostic display formats. In a display format that requests reference modifiers but is not a debug format, a read-only by-ref return is presented as `ByRef` — no less restricted than a mutable by-ref return. In the formats used by the IDE for quick info, which do not request reference modifiers, the return is presented as the element type by value, with no `ByRef` and no `ReadOnly` marker.

This is intentional. Visual Basic has no source syntax for a read-only by-reference return, and the value-shaped usage is the common case — reading dereferences automatically — so annotating the user-facing display would be noise. The display convention is consistent with the binding model (the bound type is the element type) and with the direct-assignment diagnostic, which presents the target as a value.

Two properties of the display are guaranteed:

- The synthesized marker, where shown, uses the word order `ByRef ReadOnly` — `ByRef` before `ReadOnly`. For a getter-only property whose display style shows the read-only descriptor, the descriptor `ReadOnly` appears before `ByRef` and the synthesized marker is suppressed, so the display never doubles into `ReadOnly ByRef ReadOnly`.
- The display of a *byref-like type* — `ByRef Like Structure`, specified in ByRef-Like Type Safety — is unchanged. That marker labels a **type** that must be recognized at a glance, whereas a read-only by-reference **return** is used as a value.

Tooling and analyzers read `RefKind` and `ReturnsByRefReadonly` from the semantic model; they do not infer read-only-ness from displayed text.

### For Each over byref-like enumerators

A byref-like enumerator such as `ReadOnlySpan(Of T).Enumerator` exposes `Current` as a `ref readonly` property. The `For Each` statement's pattern lookup requires a readable `Current` property that can be called with no arguments; it does not exclude properties that return by reference. Once the import exemption makes `Current` readable, the `For Each` expansion reads it through the automatic dereference, so enumeration over a `ReadOnlySpan(Of T)` works:

```vbnet
Dim s As New ReadOnlySpan(Of Integer)(New Integer() {10, 20, 30})
Dim sum As Integer = 0
For Each n As Integer In s                 ' Okay: Current is read through the ref readonly return
    sum += n
Next
```

The rules for byref-like enumerators are specified in ByRef-Like Type Safety; this specification is consistent with them.

## Soundness
[soundness]: #soundness

The invariant that motivates this feature is that a write must never store through a read-only reference. The invariant is enforced at compile time by three mutually exclusive outcomes for every write:

1. **Refusal.** A write that stores directly through the read-only reference — direct assignment, compound assignment, `Mid` assignment, or a member write whose base receiver is a read-only l-value — is refused with BC30068.
2. **Copy without write-back.** A `ByRef` argument that is a read-only l-value is passed by value through a temporary; the write-back is discarded, so the callee mutates only its own copy.
3. **Copy for reading.** A `With` receiver that is a read-only l-value is captured as a value copy to serve member reads; a member write to such a receiver is refused (item 1), not performed on the copy. A type-inferred value that is a read-only l-value is inferred as a value copy, which is freely writable.

Every write path in the language falls into one of these three categories, and each is checked at a point where the read-only status of the target is available. Because the read-only status comes from the imported metadata signature — a required `InAttribute` modifier on a by-reference return — and is exposed on the symbol, the checks are uniform across ordinary compilation and script submissions, which share a single compiler.

The converse property — that no previously valid program becomes invalid — holds because the feature is additive. Before the import exemption, a member returning `ref readonly` was entirely unusable: reading, passing, and assigning were all rejected as an unsupported member. After the exemption, reading, passing by value, and the copy-based uses compile, and the two behaviors that were previously rejected for the whole member — writing through and using as a `ref`/`out` argument — remain refused or are lowered to copies. No program that compiled and behaved correctly changes meaning.

## Drawbacks
[drawbacks]: #drawbacks

- **Read-only tracking surface.** The read-only flag must be carried on symbols and consulted at every assignment and argument-binding site. A missed wiring would allow a write to store through a read-only reference — silent corruption of read-only memory. This is the single highest-risk aspect of the feature and the reason the write matrix (direct, compound, `Mid`, `ByRef`, `With`, member chain, inference) is an acceptance criterion.

- **Silent discard vs. error.** Passing a read-only l-value to a `ByRef` parameter compiles and silently discards the callee's writes. This mirrors the existing behavior for literals and constants, but it is more subtle: the callee does execute its assignments, and only the write-back is lost. Callers who assume the callee's writes take effect may be surprised. The behavior is correct for a read-only source — a callee should not write a read-only reference — and it matches the language's copy-out culture, but a future warning could improve clarity.

- **Model difference from C#.** C# refuses the same write-through with an error and refuses read-only by-reference values as `ref`/`out` arguments outright. Visual Basic copies instead. The divergence is confined to the source surface and does not change metadata or cross-language interoperability, but the two languages enforce the read-only invariant through different mechanisms.

## Alternatives
[alternatives]: #alternatives

- **Treat `ref readonly` as a mutable `ref`.** Exempting the `InAttribute` modifier without tracking read-only-ness would make `s(0) = 42` compile and store through the read-only reference. For memory that is genuinely read-only, this silently corrupts data at run time. This alternative is rejected: it violates the invariant that a write must never store through a read-only reference.

- **Port the full C# ref-safety model.** The language could adopt `scoped`, `UnscopedRef`, and ref-escape analysis. This exceeds the positioning of the language, which consumes byref-like and read-only by-reference members without declaring them, and the feature needs only two states — readable and not writable — not a lifetime system. The cost is high and the benefit is limited to scenarios the language does not have.

- **Refuse rather than copy.** The language could report an error whenever a read-only l-value is passed as a `ByRef` argument, matching C#. This was rejected: Visual Basic's copy-out convention passes every value with no home by copy, and a read-only l-value is a value with no writable home. The copy behavior is consistent with the existing handling of literals and constants.

- **Exempt every required custom modifier.** Exempting all required modifiers would accept members whose signatures use unrelated required modifiers for other contracts, silently importing members the compiler does not understand. The exemption is deliberately confined to `InAttribute`, identified by attribute identity.

## Unresolved questions
[unresolved]: #unresolved-questions

None.

## Considerations
[considerations]: #considerations

### Read-only parameter recognition

Visual Basic maps every by-reference parameter — `in`, `ref`, and `ref readonly` alike — to `RefKind.Ref`; it does not currently distinguish a read-only parameter from a writable one. Consequently a read-only l-value passed to an `in` parameter is copied like any other `ByRef` argument, with the write-back discarded. This is always correct: the read-only memory is never written. Recognizing a read-only parameter and passing the reference directly, without a copy, is a possible future optimization; it is not part of this feature, because the copy is safe and the optimization would require reading `[IsReadOnly]`, `[Out]`, and `[RequiresLocation]` metadata and extending the write-tracking to parameters.

### Versioning and references

The feature reads metadata only; it changes which members compile, not how Visual Basic emits metadata. A library that changes a return from `ref` to `ref readonly`, or adds `in` to a virtual or delegate signature, changes which Visual Basic programs compile against it. Removing `InAttribute` from a by-reference return — making it mutable — changes a previously read-only target into a writable one. These are ordinary reference-versioning concerns on the library side.

### Cross-language correspondence

The C# equivalent of `ref readonly` returns is specified in [readonly-ref], which is part of C# 7.2. C# 12 introduced `ref readonly` *parameters* ([ref-readonly-parameters]); this feature concerns returns, whose metadata encoding has been stable since C# 7.2. The C# compiler's own metadata importer treats a required `InAttribute` modifier as benign, which this feature aligns Visual Basic with.

## Testing
[testing]: #testing

The feature is exercised by in-memory compilation tests that have no side effects: they do not perform network access, file writes, process launches, or registry access.

- **Import tests.** Members with `ref readonly` returns — indexers, `GetPinnableReference`, and byref-like enumerator `Current` — are imported and readable; members whose signatures use other required modifiers remain unsupported.
- **Write-matrix tests.** Direct assignment, compound assignment, and `Mid` assignment to a read-only l-value report BC30068; member writes inside `With` blocks and member chains whose base receiver is a read-only l-value are refused with BC30068, while reads through such receivers are served by the value capture; `ByRef` arguments copy the value and discard the write-back (the callee mutates only its copy); type inference produces a value copy. The matrix covers mutable and read-only `ByRef` receivers, member chains whose base is a read-only l-value, expression lambdas, and the compiler's draft-rewrite path.
- **Symbol tests.** Imported read-only by-reference members report `RefKind.RefReadOnly` and `ReturnsByRefReadonly = True`; Visual Basic source members always report `False`. The display format synthesizes `ByRef ReadOnly` only in pure debug formats, with the word order locked against a `ReadOnly` property descriptor, and a byref-like type still displays `ByRef Like Structure`.
- **Interop tests.** Calls to virtual and delegate members that declare `in` parameters compile after the parameter-path exemption; `For Each` over a `ReadOnlySpan(Of T)` enumerates through the read-only `Current`.

## Related Items
[related]: #related-items

- [Readonly references](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-7.2/readonly-ref.md)
- [Ref readonly parameters](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-12.0/ref-readonly-parameters.md)
- ByRef-Like Type Safety
- [Ref Struct Interfaces](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-13.0/ref-struct-interfaces.md)
- [Span safety](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-7.2/span-safety.md)
- [Restricted types in the Visual Basic specification](https://github.com/dotnet/vblang/blob/main/spec/types.md)

[readonly-ref]: https://github.com/dotnet/csharplang/blob/main/proposals/csharp-7.2/readonly-ref.md
[ref-readonly-parameters]: https://github.com/dotnet/csharplang/blob/main/proposals/csharp-12.0/ref-readonly-parameters.md
