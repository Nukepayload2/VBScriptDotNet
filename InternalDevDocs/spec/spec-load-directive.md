# Load Directives

* [x] Proposed
* [x] Prototype: Complete
* [x] Implementation: Complete
* [x] Specification: [Complete](spec-load-directive.md)

## Summary
[summary]: #summary

This specification defines the `#Load` source-loading directive of the Visual Basic scripting dialect: a directive that includes the source of another script file in the same submission. `#Load` is **not a language feature**. It is a pair of contracts. The compiler side makes `#Load "…"` a directive trivia that is legal only in the scripting dialect (`SourceCodeKind.Script`) and only before the first token of a compilation unit. The host side defines what the operand string means and whether anything is loaded at all: the language never interprets the operand, and a host that expands the directive parses each named file as its own syntax tree and submits the resulting set of trees as one submission.

The directive's effect is a set of syntax trees, not a set of references. A loaded tree is an ordinary script tree: its top-level declarations become members of the same script class as the loading file's, its top-level statements join the same initializer, it shares the submission's references and parse options, and its diagnostics carry its own file path and line numbers. The directive itself has no compiler-side state — it does not enter the declaration table and it does not invalidate the reference manager — so the only thing a compilation learns from a `#Load` is what the host decided to pass in as trees.

Expansion is the host's obligation. A compilation built directly from a tree that contains a `#Load` is inert with respect to that directive: the loaded file's declarations do not exist and no diagnostic says so. This is the single most important property of the directive to state, because it is invisible in the source and easy to mistake for a bug.

Loaded trees precede the tree that names them, and the submission result is determined by the last tree, so a `#Load`ed file cannot change the submission result by writing a trailing expression. A `Return` in any tree returns the whole submission, and in a typed submission that value is the process exit code.

The C# counterpart is the C# scripting `#load` directive. It shares the two defining properties — the language does not interpret the operand, and the directive must precede the first token — but it expands inside the compilation object and de-duplicates by resolved path. The counterpart of a `.vbx` file is a C# script (`.csx`), not a file-based program.

## Motivation
[motivation]: #motivation

A script file has no project file and carries no command line of its own: there is no `<Compile Include="…" />` element and no build step that could assemble several files. The source itself must therefore be able to name the other files that make up the program, and `#Load` is that mechanism. It decides three questions a script author meets every day — whether a loaded file's members can be called from the loading file, which file's top-level statements run first, and which file a diagnostic points at — so those answers must be specified rather than discovered.

Four properties of the directive are contract-shaped and must be stated.

First, **the operand has no language meaning**. `#Load "helpers.vbx"` and `#Load "…/lib/helpers.vbx"` are the same syntax; the difference is entirely in how the host resolves the string. A specification that described only examples would leave the search order, and the machine-dependent parts of it, unstated.

Second, **the directive is inert unless a host expands it**. The compiler parses and preserves the directive, and it reports the syntax and mode violations, but it never loads a file. A consumer that calls the compilation-construction API directly gets a compilation in which the directive did nothing and no diagnostic says so. Naming this obligation explicitly is what keeps a future reader from "fixing" a half-implemented expansion.

Third, **the directive is not a reference**. `#R` and `#Load` share a syntax shape, a position rule, and a mode gate, and they are routinely described together; but `#R` enters the declaration table and changes the reference set, while `#Load` changes the set of trees. Describing `#Load` as a second kind of reference leads directly to a wrong implementation — a source reference cannot carry top-level statements, a script class part, or an initializer entry.

Fourth, **the file's identity is a property of the host environment**. Relative operands are resolved against the file that wrote the directive, so moving a script, or loading it through a different file, changes what is loaded. The order in which trees are submitted is likewise the host's decision, and it is what determines the order of top-level execution.

## Detailed design
[design]: #detailed-design

### The directive

A load directive is a conditional-compilation directive introduced by `#` followed by the keyword `Load`:

```ANTLR
loadDirective
    : '#' 'Load' stringLiteral
    ;
```

The keyword is matched case-insensitively, so `#load` and `#Load` are the same directive. The operand is a string literal token and is required; a directive without one reports BC30217 and contributes no file to load. A directive is recognized at the beginning of a logical line, after optional whitespace.

The directive is represented by a `LoadDirectiveTriviaSyntax` node, derived from `DirectiveTriviaSyntax`, with a `LoadKeyword` child and a `File` child of type `StringLiteralToken`. The syntax kind is `LoadDirectiveTrivia`. The node has no end-of-directive token; the scanner consumes the line terminator after the directive as trivia. Because the directive is trivia rather than a statement, it occupies its own line and the line numbers of later diagnostics do not drift.

**Decision**: the spelling is `#Load` and no longer alias is defined. A second spelling for the same directive would be a second way to do the same thing, and the published spelling must remain stable for the scripts that already use it.

### Mode gating

A load directive is legal only in the scripting dialect. A `#Load` in ordinary compilation is an error; the directive is still parsed and consumed as trivia, so the tree remains well formed and the rest of the file parses normally.

```vbnet
' Compiled as ordinary code, not as a script:
#Load "helpers.vbx"   ' Error BC36967: #Load is only allowed in scripts
```

The directive is available in both faces of the dialect: a script file and an interactive submission. In an interactive session the loaded trees belong to the submission that contains the directive, so the declarations they contribute are visible in later submissions through the ordinary submission chain.

### Position: a head directive

`#Load` is a **head directive**: it is recognized only in the leading trivia of the first token of a compilation unit. A `#Load` that follows any token is still parsed as a directive, reports BC37002, and is not collected.

```vbnet
Dim x = 1
#Load "helpers.vbx"   ' Error BC37002: Cannot use #Load after first token in file
```

The position criterion is "before the first token", not "position 0": comments, whitespace, and other head directives may precede a `#Load`, and a `#Load` on any line before the first token is accepted. The criterion is applied per tree, so a `#Load` on the first line of a loaded file is legal.

The language's directive model is not uniform, and this specification makes the distinction explicit:

| Directive | Class | Position criterion |
|---|---|---|
| `#If`, `#Const`, `#Else`, `#End If`, `#Region`, `#ExternalSource`, `#Disable Warning` | free position | any logical line boundary, subject to block structure |
| `#R`, `#Load` | head | leading trivia of the first token of the compilation unit |
| `#!` | head | position 0 of the file |

**Decision**: the two head classes have different criteria for a reason. A shebang is read by the operating system kernel, which requires its two characters to be the first two bytes of the file; `#R` and `#Load` configure the compilation before it begins and need only precede any token. A script author must therefore expect a `#Load` written after a statement to be an error rather than a directive that is silently ignored.

The mode check and the position check are mutually exclusive: the mode check is applied first, so a `#Load` in ordinary compilation reports BC36967 whether or not it is positioned correctly.

### The empty operand

A directive whose operand is the empty string is a no-op: the host's expansion skips an empty operand, and the compiler reports no diagnostic.

```vbnet
#Load ""   ' Okay: no operation
```

**Decision**: this is a deliberate no-op. An empty operand is not a failure to resolve; it is the absence of an operand. The choice diverges from the C# compiler, whose `#load ""` reports CS1504; the divergence is recorded under [Divergences from the C# implementation](#divergences-from-the-c-implementation) rather than left to be inferred.

### Disabled conditional-compilation regions

A `#Load` inside a conditional region excluded by a false `#If` is not recognized as a directive at all. The scanner consumes the whole disabled region as disabled text, so no directive node is produced, no diagnostic is reported, and the host does not expand it. The mode and position checks apply only to directives in an active region. Visual Basic directive trivia has no active/inactive flag, so this boundary is a property of the scanner rather than of the node.

### What the compiler does, and does not, do

**Collection.** `CompilationUnitSyntax.GetLoadDirectives` returns the load directives of the leading trivia of the compilation unit's first token. A directive that is not in that position is not returned, even though it is still parsed and diagnosed.

**No declaration table.** The declaration-table builder records reference directives and nothing else. A `#Load` contributes no entry, and no compiler-side state records that a tree contains one.

**No reference invalidation.** A syntax tree reports that it contains a reference directive when its source-code kind is `Script` and it has at least one `#R`. `#Load` is not part of that predicate, so adding or removing a tree that contains only `#Load` does not by itself invalidate the reference manager.

**Boundary.** The directive's own contribution is nil, but a loaded tree is an ordinary script tree. If a loaded file contains a `#R`, that directive is collected like any other and the tree's presence in the compilation sets the invalidation flag when the tree is added or replaced. The two facts are consistent: the flag answers "does this tree contain a `#R`", and the loaded tree does.

**Decision**: the directive is deliberately not part of the reference mechanism. "Which trees form the submission" and "which assemblies are referenced" are orthogonal states; coupling them through one invalidation flag would make an edit to a `#Load` list rebuild the reference binding and would give a tree-level concept a reference-level name.

### The host expansion contract

A host that expands `#Load` performs the following walk. The result is the list of trees that the submission is built from.

1. Parse the script text as the **main tree**, using the script's parse options and its file path.
2. Seed the **active expansion set** with the normalized path of the main file, compared case-insensitively, when the main file has a path. A file that loads the main file is therefore a cycle.
3. For each load directive in the main tree's leading trivia, in source order:
   1. an empty operand is skipped;
   2. resolve the operand against the file path of the tree that wrote the directive;
   3. if the resolution produces no path, or the path is already in the active expansion set, stop the walk and report BC2001 anchored at the directive's string literal token;
   4. otherwise read the file and parse it as a **new tree**, with the same parse options and with the resolved path as its file path;
   5. expand that tree recursively, depth first;
   6. remove the path from the active expansion set and append the tree to the list.
4. Append the main tree last.

The walk is depth first and a file is appended after its own loaded files, so a file's top-level statements run before the statements of the file that loads it, and the main file runs last. The first failure in this order stops the walk; the trees resolved before it are discarded along with the failure, because the expansion produces either a complete tree list or one diagnostic.

**Relative paths are resolved against the referring tree.** A nested `#Load` in a loaded file resolves against that loaded file's path, not against the main file's path, so a library can be located relative to itself.

**The relative base is the only file-system input.** The operand string itself is opaque to the language and is passed to the host's source reference resolver together with the referring file's path.

**Decision**: depth-first order with each loaded tree before its referrer. This is the order the author reads and the order a text-inlining implementation would have produced, so a script that depended on the order of top-level statements keeps its behavior.

**Decision**: an expansion failure is reported at the directive, not at a location inside the missing file. The only location the author wrote is the directive, and the directive is what must change.

**Case and identity.** Directive keywords are matched case-insensitively, and path identity on the active expansion set is compared case-insensitively.

**Cycle detection covers the active expansion set only.** A path is removed from the set when its subtree has been fully expanded, so a file that is reachable by two different branches of the same walk is loaded twice. This diverges from the C# implementation, which de-duplicates by resolved path; see [Divergences from the C# implementation](#divergences-from-the-c-implementation).

### The multi-tree submission

The tree list is submitted as **one submission**, not as several. The submission model — the synthesized script class, the initializer, the result rule, and the chain of previous submissions — is unchanged; the only difference is that the script class has one part per tree.

**One declaration space.** A top-level `Dim` in any tree is a field of the script class, a top-level `Sub` or `Function` in any tree is an instance member — or a `Shared` member if declared `Shared` — and a top-level type in any tree is a nested type. A name declared in a loaded file is visible to the file that loads it and to every later tree, and a name declared in the main file is visible to the loaded files. A duplicate name in two trees is a duplicate declaration, because the trees are parts of one type.

**One initializer.** All trees contribute to one initializer sequence. Within a tree, the initializers of instance fields and properties and the top-level statements are collected into a single list in source order, so a statement that precedes a `Dim` in the same file runs before that field's initializer. Between trees the lists are concatenated whole in tree order: the loaded trees first, in the order the walk produced them, then the main tree. A loaded file's top-level statements therefore run before the main file's field initializers.

**The submission result is determined by the last tree.** The main tree is the last tree, so the shape of the main file's final statement decides whether the submission has a result, and a trailing expression in a loaded file is not a result. A `Return` in any tree is the return statement of the initializer and therefore returns the whole submission; in a typed submission — the shape a `.vbx` file is executed with — that value is the process exit code, whichever tree wrote it.

**Zero span drift.** Because every file is its own tree with its own file path, a diagnostic in a loaded file carries that file's path and line, and the main file's spans are not shifted by the text that was loaded. This is the reason the directive is a directive trivia plus a set of trees rather than a text inclusion.

**Shared references.** All trees are built with one reference set, so a loaded file can use the types the submission references without a directive of its own.

The following two files are a complete program. `helpers.vbx` is the loaded file:

```vbnet
' helpers.vbx — its own tree, with its own path, so its diagnostics carry its own line numbers
Imports System.Text                          ' in effect in this file only (see below)

Dim greeting As String = "hello"             ' a field of the script class
Dim sb As New StringBuilder("loaded")        ' a field initializer: runs before the main file's statements

Function LoadedValue() As Integer            ' an instance member of the script class
    Return 42
End Function

Print("helpers.vbx: " & sb.ToString())       ' a top-level statement: runs before the main file's statements
```

`main.vbx` is the main file:

```vbnet
' main.vbx
#Load "helpers.vbx"                          ' a directive trivia; the operand is resolved by the host
Print("main.vbx: " & LoadedValue() & " " & greeting)
Return 0                                     ' a typed submission: only an explicit Return is the exit code
```

Running the main file prints:

```text
helpers.vbx: loaded
main.vbx: 42 hello
```

and the exit code is `0`.

### `Imports` in a loaded tree

Two rules apply, and they must not be conflated.

- **Within a submission, `Imports` is per file.** The scope of an `Imports` statement specifically does not include other source files. A loaded file's `Imports` is in effect in that file and nowhere else in the submission, and the loading file does not see it.
- **Across submissions, `Imports` accumulate.** The host collects the clauses of every tree of every submission in the chain and applies them to the next submission, so a clause written in a loaded file is in effect in the next submission of the session.

References are **inherited** along the chain; `Imports` are **accumulated** by the host. The two mechanisms must not be described interchangeably.

**Decision**: a loaded file's imports are not shared with the loading file. An `Imports` statement's scope would otherwise depend on which other file happened to load it, which is a property the author of the `Imports` statement cannot see.

### Coexistence with the other head directives

`#!`, `#R`, and `#Load` may all appear in the same leading trivia. Each is parsed, each is discoverable through its directive API, and no relative order is required among them.

**Decision**: no order is imposed between `#R` and `#Load`. Their effects are independent — `#R` changes the reference set, `#Load` changes the tree set — and neither is a precondition of the other. An order rule would be a constraint with no semantic consequence.

### Runtime loading and ahead-of-time compilation

A submission is compiled to an in-memory assembly, loaded dynamically, and executed against state passed as an object array. Loaded trees are part of that submission and are compiled into the same assembly. The mechanism is outside the scope of ahead-of-time compilation and trimming and adds no ahead-of-time or trimming friction of its own.

### Errors

| Code | Diagnostic | Condition |
|---|---|---|
| BC36967 | `ERR_LoadDirectiveOnlyAllowedInScripts` | `#Load` in ordinary compilation. Message: "#Load is only allowed in scripts" |
| BC37002 | `ERR_PPLoadFollowsToken` | `#Load` after the first token of a compilation unit. Message: "Cannot use #Load after first token in file" |
| BC30217 | `ERR_ExpectedStringLiteral` | The directive has no string literal operand. Message: "String constant expected." |
| BC2001 | `ERR_FileNotFound` | The host cannot expand an active directive: the operand resolves to no file, or the resolved path is already being expanded. Message: "file '{0}' could not be found" |

The first three are compiler diagnostics. BC2001 is produced by the host during expansion, not by the compiler; it is anchored at the directive's string literal token. The expansion failure is raised as a `CompilationErrorException` carrying that single diagnostic.

BC2001 is the diagnostic of both a missing file and a cyclic load. The message says "file not found" even when the file exists and the real cause is a cycle; the divergence is recorded under [Divergences from the C# implementation](#divergences-from-the-c-implementation).

### Alignment with the C# implementation

The C# counterpart of this directive is the C# scripting `#load` directive. The two share the two defining properties — the language does not interpret the operand, and the directive must precede the first token — and they share the depth-first tree order with the loaded trees before the referrer. They differ in where the expansion lives and in what happens when the same file is reachable twice.

| Dimension | C# `#load` | Visual Basic `#Load` |
|---|---|---|
| Directive node | `LoadDirectiveTriviaSyntax`, with an end-of-directive token | `LoadDirectiveTriviaSyntax`, without one |
| Operand interpreted by the language | no | no |
| Position constraint | before the first token | before the first token |
| Mode gate | CS8097 outside a script | BC36967 outside a script |
| Position error | CS8098 | BC37002 |
| Expansion point | inside the compilation object, driven by the compilation's source reference resolver | the host, before the compilation is created |
| Reference manager reuse | `#load` counts as a directive that may affect the reference set, so a tree containing one prevents reuse of the reference manager | only `#R` counts; a `#Load` directive alone does not invalidate |
| Empty operand | `#load ""` reports CS1504 | no-op, no diagnostic |
| Missing file | CS1504, anchored at the file token | BC2001, anchored at the file token |
| Cyclic load | silently terminates: a path already loaded is not loaded again | BC2001, anchored at the directive that closes the cycle |
| Same file reachable twice | loaded once, by resolved path | loaded once per branch of the walk |
| Read failure | converted to a diagnostic (CS2015) | not converted; the resolver's exception propagates |
| Source resolver absent | CS8099 | null-reference failure |
| Tree order | loaded trees precede, the referrer is appended last | the same |
| Parse options for loaded trees | the referring tree's options | the submission's parse options |
| Directive keyword case | case-insensitive | case-insensitive |
| Active/inactive flag | directive trivia carries one | no such flag; a disabled region produces no directive node |

The rows for reference-manager reuse, the empty operand, the read failure, the absent resolver, the cyclic load, and the diamond are the semantic divergences. The row for the empty operand in particular must not be read as an alignment: the sibling `#R` directive's empty operand is discarded in both languages, but `#Load`'s empty operand is discarded only in Visual Basic.

## Soundness
[soundness]: #soundness

The directive introduces no new binding rules. It changes the set of trees that form a submission, and every tree in that set is an ordinary script tree; the language already defines what a script class with several parts means, because the script class is a partial type and its parts merge. Adding a load directive can therefore change the meaning of existing code only through the ordinary consequences of merging declarations, never by changing what any existing construct means.

The directive's reach is explicit. It is legal only in the scripting dialect, so no ordinary program can depend on host expansion without the compiler reporting it; the mode check precedes the position check, so a misplaced directive in ordinary compilation reports one error rather than two; and a misplaced directive is not collected, so the set of load directives of a tree is exactly the leading trivia of its first token.

The ordering rule is sound because it is a statement about the host's output rather than about the source: the specification fixes the order of the tree list the host must produce, and the language defines the order of a submission's initializer from its tree list. The two compose, so the execution order of top-level statements in a script with `#Load` is a function of the source.

Name resolution is unaffected. `#Load` does not import namespaces, and a loaded file's `Imports` remains file-local within the submission, so no name becomes visible in a file merely because some other file was loaded into the same submission.

## Drawbacks
[drawbacks]: #drawbacks

- **The operand's meaning depends on the environment.** Relative operands are resolved against the file that wrote them, so moving a script, loading it through a different file, or starting the host from another directory changes which file is loaded, with no diagnostic that anything changed.
- **A file reachable by two paths is loaded twice.** Cycle detection covers only the active expansion set, so a diamond loads the same file once per branch: its top-level statements execute twice and its declarations become two parts of one script class, which can produce a duplicate-declaration error inside the loaded file rather than at the directive that caused it.
- **A missing file and a cyclic load share one diagnostic.** Both report BC2001, whose message says the file could not be found; when the cause is a cycle the file is present and the message is misleading.
- **A failed expansion produces no compilation object.** The failure is raised before the compilation exists, so a tool cannot query the failed compilation for its diagnostics; it receives a single exception carrying one diagnostic.
- **A missing source resolver fails with an exception, not a diagnostic.** A compilation whose script options carry no source resolver and whose script contains a `#Load` with a non-empty operand fails with a null-reference exception rather than reporting a diagnostic.
- **An unexpanded `#Load` is inert and silent.** A consumer that builds a compilation directly from the tree gets neither the loaded declarations nor a diagnostic that they are absent.
- **Top-level semantics depend on the host's tree order.** Which file's `Dim` is a field, and which file's statements run first, follow the tree list rather than the position of the declaration in the source.
- **The host owns the walk.** A tool that needs the same semantics must reproduce the expansion, including cycle detection, path identity, and ordering; the contract is specified here precisely because there is no compiler-side state to inspect.

## Alternatives
[alternatives]: #alternatives

- **Inline the loaded file's text into the loading file.** Rejected: it shifts every span after the `#Load` by the number of inserted lines and makes a diagnostic inside the loaded file carry the wrong file and line, which is precisely the failure this directive's design avoids. The same reasoning rejects host-side stripping for the shebang directive.
- **Let `#Load` enter the declaration table as a source reference.** Rejected: the product of a `#Load` is a set of syntax trees, not a set of assembly references. Routing it through the declaration table would couple "which trees exist" and "which assemblies are referenced" into one invalidation flag, and a source reference cannot carry top-level statements, a script class part, or an initializer entry.
- **De-duplicate loaded files by resolved path.** Not adopted in the current implementation: the cycle check is scoped to the active expansion set, so a diamond loads a file once per branch. The C# compiler de-duplicates by resolved path and a cyclic load therefore terminates silently. Adopting the rule would change the behavior of existing scripts that rely on a file being executed once per branch, so it is a separate decision.
- **Report a distinct diagnostic for a cyclic load.** Not adopted in the current implementation: a missing file and a cycle are reported as the same diagnostic with the same message. The C# compiler does not report a cycle at all, so a distinct code would be a Visual Basic-specific refinement rather than an alignment.
- **Report a diagnostic for an empty operand.** Not adopted: the empty operand is a no-op, and a diagnostic would be a divergence from the current behavior with no failure to report.
- **Expand `#Load` in the compiler.** Not adopted: the tree set is an input the host supplies when it constructs the submission, and the host is the party that owns the file-system abstraction. The C# compiler expands inside the compilation object, driven by a source reference resolver carried in the compilation options; the two placements are a genuine design difference, not an oversight.
- **Use `#Load` for assembly references.** Not applicable: `#Load` includes a source file in the same submission and does not add an assembly reference.

## Unresolved questions
[unresolved]: #unresolved-questions

None.

## Considerations
[considerations]: #considerations

### Expansion is the host's obligation

The directive is a compiler-recognized syntax node and a host-defined operation, and the two halves are not interchangeable. The compiler guarantees the node, the mode gate, the position gate, the operand's syntax, and the tree API that exposes the directive; it guarantees nothing about what the operand means or whether a file is loaded. A compilation constructed from a tree that contains a `#Load` therefore contains no trace of the loaded file and reports no diagnostic about its absence. This is a contract, not a gap: the tree set of a submission is an input, and the host that builds the submission is the party that decides it.

The consequence for tooling is that a consumer which constructs a compilation directly must perform the same walk — resolve each operand against the referring tree, detect cycles, order the trees depth first — to obtain the semantics a script runner would produce. Reading the directive from the tree tells a tool what the author asked for; it does not tell the tool what was loaded.

### The failure channel

An expansion failure is raised as a `CompilationErrorException`, and the script API converts it into diagnostics at the compilation step: `Script.Compile()` catches the exception and returns its diagnostics rather than throwing, so a caller that compiles before running receives a diagnostic list. The hosts built on that API therefore behave as follows.

- **Interactive.** The session compiles the submission first; if the compilation reports errors, the submission is dropped and the session continues. A misspelled file name in an interactive `#Load` reports a diagnostic and does not end the session.
- **Script file.** The file path does not compile first, so the exception is raised while running; the host catches it, reports its diagnostics, and returns a failure exit code.
- **Check-only mode.** The check path uses the same compile step as the interactive path, so the exit-code contract — zero when there are no compilation errors, non-zero when there are — holds when a `#Load` fails to expand.

The residual difference from C# is structural: because the failure occurs before the compilation object is created, a failed submission has no compilation to query, whereas the C# compiler has a compilation object whose diagnostics include the load failure. This is visible to an analysis tool and not to a command-line user.

### Divergences from the C# implementation

Six divergences are stated here rather than left to be discovered, because each of them is a place where a reader who knows C# would predict the wrong behavior.

- **The reference manager is not invalidated by `#Load`.** C# treats `#load` as a directive that may affect the reference set, because a loaded file can carry a `#r`; Visual Basic's invalidation predicate asks only whether a tree has a `#R`. The difference is observable only as a reuse decision inside the compiler, and it does not change the reference set of a compilation: a loaded tree that carries a `#R` still contributes that reference and still sets the flag.
- **An empty operand is silent.** C# reports CS1504 for `#load ""`; this dialect skips it.
- **A read failure is not converted to a diagnostic.** C# wraps the failure while reading a resolved file into a diagnostic (CS2015) anchored at the directive; this dialect does not guard the read, so an exception raised by the resolver propagates out of the expansion and out of the script compilation step, which converts only `CompilationErrorException`.
- **An absent source resolver fails with an exception.** C# reports CS8099 when the compilation has no source reference resolver and the script contains a `#load`; this dialect does not guard the resolver, so the same input fails with a null-reference exception.
- **A cyclic load is diagnosed.** C# de-duplicates by resolved path, so a cycle terminates silently; this dialect detects a cycle on the active expansion set and reports BC2001 at the directive that closes the cycle.
- **A diamond is not de-duplicated.** C# loads a file that is reachable twice once, by resolved path; this dialect loads it once per branch of the walk, so its top-level statements execute twice and its declarations become two parts of one script class.

### Path identity is case-insensitive

The active expansion set compares paths case-insensitively. A load chain that alternates the case of a file name is therefore detected as a cycle, and two spellings that differ only in case are the same file for cycle detection. The rule matches the case-insensitivity of the language's identifiers and of the directive keyword.

### Diagnostic text for a cyclic load

BC2001's message says the file could not be found, and a cyclic load reports the same message even though the file is present. The message is accurate for the missing-file case, which is the common one; for a cycle it names the file whose load closed the cycle. An author who sees the message on a file that exists should read the diagnostic's location — the directive that closes the cycle — rather than the file name in the message.

## Testing
[testing]: #testing

The feature is exercised by in-process tests that have no side effects: they do not perform network access, process launches, registry access, or file writes outside an isolated temporary directory. Most cases use an in-memory source resolver, so no file is read from disk at all.

- **End-to-end script tests.** A `.vbx` file whose first line is a load directive calls a function declared in the loaded file and exits with code zero. An interactive session that loads a file can call the loaded file's function in a later submission and prints its value, and a load directive on the first line of a loaded file is accepted.
- **Nested-expansion and result tests.** A loaded file that itself loads a third file compiles, and a function declared in the deepest file is callable from the file that loads the middle file. A loaded file whose only statement is `Return 17` makes the submission result `17`.
- **Span and location tests.** A diagnostic in the main file after a load directive reports the main file's real line rather than a shifted line. A diagnostic inside a loaded file reports the loaded file's real path and line.
- **Failure tests.** A load directive naming a file that does not exist raises a `CompilationErrorException` whose single diagnostic is anchored at the load directive's line in the referring file. A load cycle is reported at the load directive that closes the cycle, in the file that wrote it.
- **Reference and import tests.** A loaded file resolves types from the submission's references without a directive of its own. An `Imports` clause written in a loaded file is replayed into the next submission of the session, and a malformed accumulated clause is reported by the submission gate rather than raised as an exception.
- **Directive-coexistence tests.** A shebang, a reference directive, and a load directive in one leading trivia all parse, each is discoverable through its directive API, and the loaded code runs.
- **Position-gate tests.** A load directive after the first token reports BC37002, and a load directive in the first line reports no position diagnostic.

## Related Items
[related]: #related-items

- [Preprocessing Directives](https://github.com/dotnet/vblang/blob/main/spec/preprocessing-directives.md) — the free-position directive model that the head directives are distinguished from
- [Source Files and Namespaces](https://github.com/dotnet/vblang/blob/main/spec/source-files-and-namespaces.md) — the `Imports` scope rule and the entry-point rule
- [Types](https://github.com/dotnet/vblang/blob/main/spec/types.md) — partial declarations, which is what lets a script class span several trees
- [Script&lt;T&gt;](https://github.com/dotnet/roslyn/blob/main/src/Scripting/Core/Script.cs) — the shared scripting API whose compile step converts a failed expansion into diagnostics
- [SourceReferenceResolver](https://github.com/dotnet/roslyn/blob/main/src/Compilers/Core/Portable/SourceReferenceResolver.cs) — the host-supplied resolver that turns an operand into a path and a text
- [SyntaxAndDeclarationManager](https://github.com/dotnet/roslyn/blob/main/src/Compilers/CSharp/Portable/Compilation/SyntaxAndDeclarationManager.cs) — the C# `#load` expansion, including its path de-duplication
- [LoadDirectiveTests](https://github.com/dotnet/roslyn/blob/main/src/Compilers/CSharp/Test/Symbol/Compilation/LoadDirectiveTests.cs) — the C# reference tests for `#load`, including the empty-operand, undecodable-file, and cycle cases
