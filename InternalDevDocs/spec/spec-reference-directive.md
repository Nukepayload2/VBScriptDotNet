# Reference Directives

* [x] Proposed
* [x] Prototype: Complete
* [x] Implementation: Complete
* [x] Specification: [Complete](spec-reference-directive.md)

## Summary
[summary]: #summary

This specification defines the `#R` reference directive of the Visual Basic scripting dialect: a directive that names an assembly for a script compilation. `#R` is **not a language feature**. It is a pair of contracts. The compiler side makes `#R "…"` a directive trivia that is legal only in the scripting dialect (`SourceCodeKind.Script`) and only before the first token of a compilation unit. The host side defines what the operand string means: the language never interprets it, and a reference resolver supplied by the host decides which assembly, or which assemblies, the operand denotes.

The directive's effect is a set of metadata references. A single `#R` may contribute more than one reference: index 0 is the **primary asset** the operand named, and the remaining entries are that asset's **dependency closure**. Every contributed reference is an explicit reference of the compilation, and a submission inherits the explicit references of the previous submission, so a closure type remains usable in later submissions without a second directive.

References may carry aliases. The scripting host aliases the host object's assembly with `<host>` and aliases an assembly substituted for a missing dependency with `<implicit>`; the compiler respects those aliases, so the metadata of an aliased assembly is not visible at the source level unless it is exposed through the global alias. Visual Basic has no `extern alias` construct, so a reference that carries a non-global alias cannot be reached from Visual Basic source at all.

The C# counterpart is the C# scripting `#r` directive, which shares the two defining properties: the language does not interpret the operand, and the directive must precede the first token. The counterpart of a `.vbx` file is a C# script (`.csx`), not a file-based program.

The `#R "nuget:…"` and `#R "project:…"` operand prefixes are host extensions of this directive. Their semantics are outside the scope of this specification.

## Motivation
[motivation]: #motivation

A script file has no project file and carries no command line of its own: there is no `<Reference Include="…" />` element and no `/r:` option attached to the file. The source itself must therefore be able to name the assemblies the script uses. `#R` is that mechanism: it is the only source-level assembly reference of the scripting dialect, and the package and project reference forms are operand prefixes of the same directive.

Three properties of the directive are contract-shaped and must be specified rather than left to the reader.

First, **the operand has no language meaning**. `#R "System.Text.RegularExpressions"` and `#R "…\libs\Contoso.dll"` are the same syntax; the difference is entirely in how the host resolves the string. A specification that described only examples would leave the actual search order, and the machine-dependent parts of it, unstated.

Second, **the resolution order is decided by the shape of the operand string**, not by any declaration. The predicate that separates a file path from an assembly name is purely textual, and it makes `System.Text.RegularExpressions` and `System.Text.RegularExpressions.dll` take different search paths for the same assembly. A reader cannot infer this from the language specification, and the compiler reports no diagnostic when the two forms resolve differently.

Third, **the aliasing rule changes what names exist**. A reference that the host aliases does not contribute its namespaces to unqualified lookup. The symptom of this rule is not an ambiguity error but a name that appears not to exist, which is why the rule must be stated rather than discovered.

## Detailed design
[design]: #detailed-design

### The directive

A reference directive is a conditional-compilation directive introduced by `#` followed by the keyword `R`:

```ANTLR
referenceDirective
    : '#' 'R' stringLiteral
    ;
```

The keyword is matched case-insensitively, so `#r` and `#R` are the same directive. The operand is a string literal token and is required; a directive without one is a syntax error and is not collected. A directive is recognized at the beginning of a logical line, after optional whitespace.

The directive is represented by a `ReferenceDirectiveTriviaSyntax` node, derived from `DirectiveTriviaSyntax`, with a `ReferenceKeyword` child and a `File` child of type `StringLiteralToken`. The syntax kind is `ReferenceDirectiveTrivia`. Because the directive is trivia rather than a statement, it occupies its own line and the line numbers of later diagnostics do not drift.

**Decision**: the spelling is `#R` and no longer alias is defined. A second spelling for the same directive would be a second way to do the same thing, and the published spelling must remain stable for the scripts that already use it.

### Mode gating

A reference directive is legal only in the scripting dialect. A `#R` in ordinary compilation is an error; the directive is still parsed and consumed as trivia, so the tree remains well formed and the rest of the file parses normally.

```vbnet
' Compiled as ordinary code, not as a script:
#R "System.Text.RegularExpressions"   ' Error BC36964: #R is only allowed in scripts
```

Directives are collected from every tree whose source-code kind is not `Regular`, while the invalidation flag described under [Collection and incremental rebinding](#collection-and-incremental-rebinding) is set only for a tree whose source-code kind is `Script`.

### Position: a head directive

`#R` is a **head directive**: it is recognized only in the leading trivia of the first token of a compilation unit. A `#R` that follows any token is still parsed as a directive, reports an error, and is not collected.

```vbnet
Dim x = 1
#R "System.Text.RegularExpressions"   ' Error BC36959: cannot use #r after first token in file
```

The position criterion is "before the first token", not "position 0": comments, whitespace, and other head directives may precede a `#R`, and a `#R` on any line before the first token is accepted.

The language's directive model is not uniform, and this specification makes the distinction explicit:

| Directive | Class | Position criterion |
|---|---|---|
| `#If`, `#Const`, `#Else`, `#End If`, `#Region`, `#ExternalSource`, `#Disable Warning` | free position | any logical line boundary, subject to block structure |
| `#R`, `#Load` | head | leading trivia of the first token of the compilation unit |
| `#!` | head | position 0 of the file |

**Decision**: the two head classes have different criteria for a reason. A shebang is read by the operating system kernel, which requires its two characters to be the first two bytes of the file; `#R` and `#Load` configure the compilation before it begins and need only precede any token. A script author must therefore expect a `#R` written after a statement to be an error rather than a directive that is silently ignored.

### The empty operand

A directive whose operand is the empty string is discarded before it reaches the declaration table: it is not a directive, contributes no reference, and reports no diagnostic.

```vbnet
#R ""   ' Okay: no operation
```

**Decision**: this is a deliberate no-op, and it mirrors the C# compiler, whose declaration-table builder applies the same predicate. An empty operand is not a failure to resolve; it is the absence of an operand. Reporting a diagnostic instead would be a divergence from C# and would require its own justification and language-version gating.

### Disabled conditional-compilation regions

A `#R` inside a conditional region excluded by a false `#If` is not recognized as a directive at all. The scanner consumes the whole disabled region as a single disabled-text trivia, so no directive node is produced and no diagnostic is reported. The mode and position checks apply only to directives in an active region. This is the same boundary that applies to the shebang directive.

### Collection and incremental rebinding

Directives are collected while the declaration table is built, not while binding. The declaration-tree builder walks the reference directives of the compilation unit, discards any whose string literal carries a diagnostic or whose value text is empty, and records the remainder as an operand text together with the directive's source location on the root declaration. The declaration table merges the directives of all root declarations into the compilation's directive set. Collection happens for every non-`Regular` tree; a `Regular` tree contributes an empty set.

The compilation exposes the collected directives and uses their presence as an invalidation flag. A syntax tree reports that it contains a reference directive when its source-code kind is `Script` and it has at least one `#R`. Adding, removing, or replacing a tree accumulates that flag, and the compilation reuses its reference manager only when the flag is false.

The flag answers "does this tree contain a `#R`", not "did the directive set change". Replacing a tree that contains a `#R` with a tree that contains an identical `#R` still rebuilds the reference binding.

**Decision**: the invalidation is deliberately coarse. Correctness requires that a change to the directive set is never missed, and the declaration table can decide that the reference set may have changed before any binding occurs, which is what lets the compilation avoid rebinding references. The cost — editing a script that contains a `#R` rebuilds the reference manager — is a known performance boundary, not a defect.

### The host resolver contract

The operand string is interpreted by a `MetadataReferenceResolver` supplied through the compilation options. The compiler asks the resolver to resolve the operand, using the file path of the syntax tree that contains the directive as the base path, and requests assembly references with recursive aliases enabled, so an alias applied to a directive reference propagates to the assemblies it references.

The resolver is chosen by the host and depends on how the compilation is run:

- **Script runner.** A script runner installs the command-line reference resolver, which resolves any path or assembly name the operand may contain.
- **Ordinary compilation.** A compiler invoked to produce an assembly installs a resolver that restricts directive references to the assemblies already supplied on the command line. It resolves the operand and then keeps only the results whose assembly identity matches one of those references.

**Decision**: the restriction in ordinary compilation is intentional. When a script is included in a project, a `#R` must not be able to introduce a dependency that the project did not declare. The same directive therefore has different reach under the two hosts, and that is a property of the two hosts rather than a defect of the directive.

### Resolution order

When the scripting host's resolver is asked to resolve an operand, it searches in the following order. If a step produces a reference, the search stops.

1. **Package prefix.** If the operand has the package-reference prefix that the host's package resolver recognizes, resolution is delegated to that resolver. If the host has no package resolver configured, the directive resolves to no references and the search does not continue. The semantics of the prefix are outside the scope of this specification.
2. **Path.** Otherwise, if the operand is a file path by the criterion below:
   1. if the operand contains no directory separator and a trusted-platform-assembly (TPA) table is available, look up the operand's file name without its extension in that table;
   2. otherwise, or if step 1 did not match, resolve the operand as a path, in turn relative to the base path, relative to the host's base directory, and relative to each configured search path.
3. **Assembly name.** Otherwise, look up the operand in the global assembly cache, and then parse it as an assembly display name and look up its simple name in the TPA table.

If no step produces a reference, the directive resolves to no references and the compiler reports BC2017, "could not find library '{0}'", at the directive.

**The path criterion.** An operand is a file path when its extension — the text after the last period that follows the last directory separator — is `.dll` or `.exe`, compared case-insensitively, **or** when it contains a directory separator (`\` or `/`). The criterion is not "contains a separator": `System.Text.RegularExpressions.dll` is a path although it has none, and `System.Text.RegularExpressions` is not a path although it looks like one, because its extension is `.RegularExpressions`. Adding or removing `.dll` therefore changes which search steps run, and the compiler reports no diagnostic either way.

**Missing assemblies.** The operand itself is resolved by the order above. A dependency of a referenced assembly that the compilation does not otherwise reference is resolved by a **separate** contract with a **different** order:

1. the global assembly cache, for a strong-named identity only;
2. the TPA table, by simple name;
3. the directory of the reference that declared the dependency.

**Decision**: the two orders are specified separately and are not unified. The operand is what the author wrote, so the path branch searches the author's directory and the configured search paths; a missing dependency is discovered while reading metadata, so its search ends at the assembly that declared it. A script that must resolve identically under a given host should name a path relative to the script, or a bare name of an assembly in that host's platform assemblies; a bare assembly display name that is expected to be found in the global assembly cache is not reproducible.

### One directive, several references

A resolver may return more than one reference for a single operand. The first returned reference is the primary asset — the assembly the operand named — and the remaining references are that asset's dependency closure. Every returned reference is added to the compilation's explicit references, and all of them are anchored at the same directive location, in the order the resolver returned them.

The per-directive map that the compiler keeps internally — it is not part of the public API — holds only the primary asset for each directive. It answers "which assembly did this directive name", not "which references did this directive contribute".

The complete set contributed by directives is observable through the public `Compilation.DirectiveReferences` and `Compilation.References`. A reference that appears in that view is not necessarily an assembly the source named: a closure entry is contributed by a directive the source wrote, but its assembly is not spelled in the source.

**De-duplication.** Two directives are the same directive when they have the same operand text and the same containing file path; the second occurrence is not resolved again. A tree without a file path contributes the empty string as the file-path component, so two directives with the same operand text in two path-less trees of one compilation are treated as duplicates and resolved once. A directive that resolves to an already-referenced assembly is allowed and is not an error.

The reference set is enumerated in reverse registration order. The array holds a compilation's directive references first, then its external references, and then the explicit references inherited from the previous submission, so the inherited references are registered first, external references next, and directive references last. An entry that the compilation already references — for example because an external reference names the same file — is de-duplicated: it keeps no directive location (its location is `Location.None`) and does not appear in `Compilation.DirectiveReferences`. Whether a closure entry is observable through the directive view therefore depends on whether the compilation already references it.

### Cross-submission inheritance

Each submission is a separate compilation, and a submission's reference set includes every explicit reference of the previous submission. A closure introduced by a directive in an earlier submission therefore remains available in later submissions without repeating the directive.

References are **inherited**; `Imports` are **accumulated**. The host clears references and imports when it continues a script and then re-applies them through their respective mechanisms: the reference set travels with the compilation chain, while an `Imports` clause is replayed by the host on each submission. The two must not be described interchangeably.

### Reference aliases

A metadata reference may carry aliases. The compiler respects them: an assembly whose aliases are empty, or whose aliases include `global`, contributes its global namespace to the merged namespace, and an assembly that carries any other alias does not. An alias is applied to a reference by the host or the toolchain; no source construct can attach one.

Two aliases are applied by the scripting host:

- **`<host>`** is applied to the host object's assembly, with recursive aliases. It keeps the host object's namespaces and global types out of unqualified lookup in a script.
- **`<implicit>`** is applied to an assembly that the resolver substitutes for a missing dependency. It lets the assembly satisfy an assembly identity without merging its types into the global namespace, where they could make an otherwise unambiguous name ambiguous.

**No escape hatch.** Visual Basic has no `extern alias` construct, and the compiler provides no other syntax that can name an aliased reference. A reference that carries a non-global alias is therefore unreachable from Visual Basic source: the hiding is absolute. This is a deliberate asymmetry with C#, whose `extern alias` can reach such a reference. It is also an exception to the language's usual treatment of an unresolvable import, which is a warning rather than silence; the exception is justified because an author cannot create an alias, only be affected by one.

**Ordinary compilation uses the same rule.** The alias filter is not specific to scripts. Under `/nostdlib` the command-line compiler applies a non-global alias to the real core library it substitutes for the facades, so that the library's type surface does not merge into the global namespace and the set of "type not defined" errors is unchanged. The filter is therefore functional semantics for ordinary compilation as well.

### `#R` does not import namespaces

A reference directive adds assemblies to the compilation. It does not bring any namespace into scope, and it does not make any type name available unqualified. `Imports` is the construct that does that.

```vbnet
#R "System.Text.RegularExpressions"

Dim m = Regex.Match("vbi 2.0", "\d+\.\d+")   ' Error BC30451: 'Regex' is not declared
```

```vbnet
#R "System.Text.RegularExpressions"
Imports System.Text.RegularExpressions

Dim m = Regex.Match("vbi 2.0", "\d+\.\d+")   ' Okay
```

The two constructs are complementary: `#R` is the assembly face and `Imports` is the name face. A script that needs a library usually needs both.

### Runtime loading and ahead-of-time compilation

When a submission is compiled, the script host registers each reference with its assembly loader: it walks the bound reference manager's referenced assemblies and registers the file path of every reference that has one, keyed by assembly identity. Every reference a directive contributed — the primary asset and the closure — is registered, which is what makes a closure type usable at run time in a later submission. At run time the host resolves the assembly by identity, not by directive.

A script submission is compiled to an in-memory assembly, loaded dynamically, and executed against state passed as an object array. It is outside the scope of ahead-of-time compilation and trimming, and the reference directive adds no ahead-of-time or trimming friction of its own.

### Errors

| Code | Diagnostic | Condition |
|---|---|---|
| BC36964 | `ERR_ReferenceDirectiveOnlyAllowedInScripts` | `#R` in ordinary compilation. Message: "#R is only allowed in scripts" |
| BC36959 | `ERR_PPReferenceFollowsToken` | `#R` after the first token of a compilation unit. Message: "Cannot use #r after first token in file" |
| BC2017 | `ERR_LibNotFound` | The resolver produced no reference for an active directive. Message: "could not find library '{0}'" |
| BC37002 | `ERR_PPLoadFollowsToken` | `#Load` after the first token of a compilation unit. Message: "Cannot use #Load after first token in file" |

The mode error and the position error are mutually exclusive: the mode check is applied first, so a `#R` in ordinary compilation reports BC36964 whether or not it is positioned correctly. BC37002 is the position error of the sibling `#Load` head directive; the two directives share the position rule and differ only in the code they report.

### Alignment with the C# implementation

The C# counterpart of this directive is the C# scripting `#r` directive. The two share the two defining properties: the language does not interpret the operand, and the directive must precede the first token of the compilation unit. The mode gate and the empty-operand filter are aligned as well.

| Dimension | C# | Visual Basic |
|---|---|---|
| Reference directive | `#r "…"` (C# scripting) | `#R "…"` (`ReferenceDirectiveTrivia`) |
| Operand interpreted by the language | no | no |
| Position constraint | before the first token | before the first token |
| Mode gate | CS7011 outside a script | BC36964 outside a script |
| Position error | CS7009 | BC36959 |
| Empty operand | discarded | discarded |
| Package reference form | the ignored directive `#:package id@version`, and the scripting form `#r "nuget:…"` | the operand prefix `#R "nuget:…"` |

Three differences must not be conflated. The position rule quoted from the C# 14 [ignored-directives] proposal belongs to the ignored directives `#!` and `#:`, not to `#r`; the `#r` position check is part of the C# `#r` implementation. The ignored-directives proposal also imposes a constraint that C# `#r` does not have — an ignored directive must precede any `#if` — and neither C# `#r` nor Visual Basic `#R` has that constraint. Finally, C# ignored directives are legal only under the file-based-program parse option, whereas Visual Basic has no file-based-program mode; the counterpart of a `.vbx` file is a C# script, and `#R "nuget:…"` and `#:package id@version` are not interchangeable.

## Soundness
[soundness]: #soundness

The directive introduces no new binding rules. It changes only the set of assemblies a compilation references, and the language already defines how a reference set affects name lookup. Adding a directive can therefore change the meaning of existing code only by introducing an ambiguity that the language already handles, not by changing what any existing construct means.

The two gates make the directive's reach explicit. A `#R` in ordinary compilation is an error, so no ordinary program can depend on host resolution without the compiler reporting it. A misplaced `#R` is diagnosed and is not collected, so the reference set of a compilation is exactly the set of directives in the leading trivia of the first token of its script trees.

Aliasing is sound with respect to name lookup because it is applied uniformly: the same predicate decides, for every reference, whether its global namespace participates in the merge. The only producers of a non-global alias are the host and the toolchain, so the situation an author can be affected by cannot be created by source.

## Drawbacks
[drawbacks]: #drawbacks

- **The same directive means different things under the two hosts.** A script runner resolves any path or name; an ordinary compilation keeps only references the command line already supplied. A script moved into a project therefore fails or resolves against a different assembly, and no language change can remove that gap.
- **Bare-name resolution depends on the host process.** The same directive can resolve to different assemblies on different machines, with no diagnostic. A script is not self-contained; its reference set is partly a property of the environment it runs in.
- **Relative paths depend on the launch context.** A path operand is resolved against the script's file path, the host's base directory, and the configured search paths, so moving the script or starting the host from another directory changes the result.
- **A directive is no longer one-to-one with a reference.** The references a directive contributes share its location, so a diagnostic about a closure entry — including a metadata read failure — points at the directive rather than at a location inside the closure, and the per-directive map exposes only the primary asset. Anchoring at the directive is preferable to a diagnostic with no location, but the diagnostic is anchored at the directive the source wrote rather than at the failing assembly. Tooling that reconstructs a compilation's reference set from the public view will see assemblies the source never named.
- **Aliasing is absolute.** A reference that carries a non-global alias cannot be reached from source, and the symptom is a name that appears not to exist. There is no syntax an author can use to recover it.
- **Invalidation is coarse.** Any edit to a tree that contains a `#R` rebuilds the reference binding, because the flag records the presence of a directive rather than a change to the directive set.

## Alternatives
[alternatives]: #alternatives

- **Strip the `#R` lines in the host and translate them to command-line reference options.** Rejected: it shifts every diagnostic line number, duplicates the parser's position and mode decisions in the host, and hides the directive from editors and language servers, which see the same tree as the compiler. The same reasoning rejects host-side stripping for the shebang directive.
- **Let a directive resolve arbitrary paths and names in ordinary compilation.** Rejected: the ordinary compilation host deliberately restricts directive references to the assemblies supplied on the command line, so that including a script in a project cannot introduce an undeclared dependency.
- **Keep the one-reference limit per directive.** Rejected: the package and project reference forms each need one directive to contribute a primary asset and its dependency closure in a single step. Multi-reference resolution is dormant for zero- and one-reference results and leaves the per-directive view unchanged, so it is purely additive.
- **Do not alias the host and substituted references.** Rejected: the host alias exists to keep the host object's namespaces and global types out of unqualified lookup, and the implicit alias keeps a substituted assembly from merging types into the global namespace and creating ambiguities. Removing either changes what names resolve in a script.
- **Collect directives while binding instead of in the declaration table.** Rejected: the declaration table can decide that the reference set may have changed before any binding occurs, which is what lets a compilation reuse its reference manager. Collecting at binding time would rebind references on every compilation.
- **Use `#Load` for assembly references.** Not applicable: `#Load` includes a source file in the same compilation and does not add an assembly reference.
- **Report a diagnostic for an empty operand.** Not adopted: the empty operand is a no-op in both languages, and a diagnostic would be a divergence from C# that would need its own justification and language-version gating.

## Unresolved questions
[unresolved]: #unresolved-questions

None.

## Considerations
[considerations]: #considerations

### Why this is not a language feature

The language specification's directive model covers conditional compilation, external source, and regions; it has no source-level assembly reference syntax, and the compilation-unit grammar has no slot for a directive. Assembly references are a property of the compilation environment, and `#R` stays outside the language in the same way: it is trivia, it is legal only in the scripting dialect, and in ordinary compilation it is an error rather than a second way to reference an assembly. That placement is what allows the directive's operand to be defined by the host without the language taking a dependency on any particular package or file-system model.

### Reproducibility of an operand

Because the operand's meaning is host-dependent, the choice of operand form is a portability decision. A form whose search order includes the global assembly cache or an unqualified platform lookup can resolve differently under a different host; a path relative to the script, or a bare name of an assembly in the host's platform assemblies, is reproducible under that host. The specification states the exact order so that a reader can determine which forms are host-dependent rather than having to infer it from examples.

### The per-directive view and the complete view

Two different questions must not be conflated. "Which assembly did this directive name" is answered by the compiler's internal per-directive map, which holds the primary asset; that map is not part of the public API, so a tool that needs the author's intent reads the directives themselves through the syntax tree's directive API. "Which references did this compilation obtain from directives" is answered by the public directive-reference view, which contains every reference a directive contributed that was not de-duplicated.

## Testing
[testing]: #testing

The feature is exercised by in-process tests that have no side effects: they do not perform network access, process launches, registry access, or file writes outside an isolated temporary directory. Test fixtures are the test assembly itself, an assembly built in memory, or an assembly emitted to a per-test temporary directory.

- **Resolution tests.** A directive naming a library on disk makes the library's type usable in the same submission and in a later submission of the same session. The same directive written twice is accepted and contributes the reference once. A directive naming a file that does not exist reports BC2017 at the directive. A dependency of a referenced library that lives in the same directory resolves without a directive of its own.
- **Multi-reference tests.** A directive whose resolver returns a primary asset plus a closure makes a closure type bindable in a later submission that repeats no directive. Two directives that each expand to a valid primary asset and an unreadable reference report BC31519 at their own lines, and an ordinary directive interleaved between them is unaffected.
- **Directive-parsing tests.** A shebang, a reference directive, and a load directive coexist in one script file: all three parse, each is discoverable through its directive API, and the loaded code runs.
- **Zero-regression tests.** An ordinary script whose directive names a platform assembly compiles and runs through the interactive seam with no package resolver configured, and no restore is attempted.

## Related Items
[related]: #related-items

- [ignored-directives](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-14.0/ignored-directives.md) — the C# 14 proposal that introduces `#!` and `#:` as ignored preprocessing directives, including its reference to `#r`
- [Preprocessing Directives](https://github.com/dotnet/vblang/blob/main/spec/preprocessing-directives.md) — the free-position directive model of the language
- [Source Files and Namespaces](https://github.com/dotnet/vblang/blob/main/spec/source-files-and-namespaces.md) — the `Imports` statement, which is what makes a referenced namespace's names available
- [Script&lt;T&gt;](https://github.com/dotnet/roslyn/blob/main/src/Scripting/Core/Script.cs) — the shared scripting API
- [Compilation](https://github.com/dotnet/roslyn/blob/main/src/Compilers/Core/Portable/Compilation/Compilation.cs) — the public reference view of a compilation

[ignored-directives]: https://github.com/dotnet/csharplang/blob/main/proposals/csharp-14.0/ignored-directives.md
