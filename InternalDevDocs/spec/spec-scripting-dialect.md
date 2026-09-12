# The Visual Basic Scripting Dialect

* [x] Proposed
* [x] Prototype: Complete
* [x] Implementation: Complete
* [x] Specification: [Complete](spec-scripting-dialect.md)

## Summary
[summary]: #summary

This specification defines the **declaration and submission model** of the Visual Basic scripting dialect — the dialect used for script files and for the interactive window. The dialect has no declaration syntax of its own. Under `SourceCodeKind.Script` the compiler synthesizes a **script class** and maps the four top-level declaration forms onto its members:

| Top-level form | Maps to |
|---|---|
| `Dim x = …` | a **field** of the script class |
| `Sub` / `Function` | a member of the script class — an **instance member**, or a **`Shared` member** if declared `Shared` |
| `Class` / `Module` / `Structure` / `Interface` / `Enum` / `Delegate` | a **nested type** of the script class |
| an executable statement | an entry of the script class **instance initializer** |

The script class is synthesized, never written in source. Its name is the `ScriptClassName` compilation option, which defaults to `Script`; in a submission the host replaces it with `Submission#N`, a name that cannot be spelled in source because `#` is a type character.

Each submission is an independent compilation. Submissions are chained through `PreviousScriptCompilation`, and a name lookup in a submission searches the script class of every previous submission and the host object type. State is preserved across submissions because a top-level `Dim` is a field, not a local. The entry point is synthesized as an asynchronous initializer, `<Initialize>`, which returns `Task(Of T)`; a non-submission script compilation wraps it in a synchronous `<Main>`, and a submission wraps it in `<Factory>`, a `Private Shared Function` whose return type is the initializer's return type.

Top-level `Await` and top-level `AddHandler` / `RemoveHandler` need no language extension: the initializer is an ordinary async method and the top-level statements are its body, so the ordinary rules apply.

The C# counterpart of the declaration and state model is the **C# scripting dialect** (`.csx`, the shared `Script<T>` API); the counterpart of the file-execution result is **C# top-level statements**; the counterpart of the interactive printing behavior is **C# interactive**. Wherever a C# comparison appears below, the specification states which of the three faces it refers to.

## Motivation
[motivation]: #motivation

Ordinary Visual Basic requires code to be inside a `Module` or `Class`. A script or an interactive session requires the opposite: a declaration typed at the top level must be usable immediately and must still be usable in the next submission. The C# language design notes state the requirement directly — "Submission system allows state preservation across evaluations" ([LDM-2020-01-22][ldm-2020-01-22]) — and place the scripting scenario alongside simple programs and top-level functions. The Visual Basic design notes recorded the same scenario as issue [#102](https://github.com/dotnet/vblang/issues/102), "Support Top-Level Statements in a Single Entry-Point File", and the direction chosen there was to reconcile the standard and scripting dialects rather than to let them drift apart ([vbldm-2017-12-06][vbldm-2017-12-06]).

This specification is the reconciliation for Visual Basic. The dialect does not add a second way to declare anything:

- The four top-level forms keep their ordinary spelling and meaning; only their container changes. `Dim` is already a legal modifier of a variable member ([type-members][vblang-type-members]), and local variables are defined as equivalent to instance variables "declared in the same way" ([statements][vblang-statements]). A top-level `Dim` is therefore the same construct as a member declaration, not a new one.
- The compilation environment is allowed to supply the entry point: "The compilation environment may also create an entry point method if one does not exist" ([source-files-and-namespaces][vblang-source-files-and-namespaces]).
- Top-level `Await` is not a relaxation of any rule. It is `Await` inside an async method, because the top-level statements are the body of the synthesized asynchronous initializer.

Two properties distinguish this dialect from the ordinary language and must be stated rather than inferred. First, `Imports` statements accumulate across submissions, which is an explicit deviation from the rule that the scope of an `Imports` statement "does not include other source files" ([source-files-and-namespaces][vblang-source-files-and-namespaces]). Second, the scripting dialect selects permissive compilation options by default — `Option Strict Off` with `Option Infer On` and `Option Explicit On` — which is the concrete carrier of the loose, late-bound heritage of the language.

## Detailed design
[design]: #detailed-design

### The script class

#### Generation and naming

When the source-code kind of a syntax tree is not `Regular`, the declaration-table builder does not create an implicit class for the compilation unit; it creates a **script class**. The declaration kind is `Submission` for a submission and `Script` for any other script compilation; the difference matters and is used throughout this specification (see [Script classes and submission classes](#script-classes-and-submission-classes)). The modifiers are fixed: `Friend`, `Partial`, and `NotInheritable`. `Shared` is a member modifier and not a type modifier, so a class has no `Shared` form. A script class is an ordinary class and not a standard module: a standard module's members are implicitly `Shared` and the module can never be instantiated ([types][vblang-types]), whereas a script class is instantiated — once by a submission's `<Factory>`, once by a script file's `<Main>` — and its top-level members are instance members unless they are declared `Shared`. The script class is, however, one of the two kinds of type that may declare extension methods, the other being a standard module: `<Extension>` on a top-level `Shared Function` declares an extension method in the ordinary way, while the same attribute on a member of a nested class is rejected with BC36551.

The name of the class is the value of the `ScriptClassName` compilation option, whose default is `Script`. The name may be dotted; each leading component becomes a namespace that wraps the class. The name is a compilation option, not a source declaration: `Script` is a normal identifier and may be referenced from source, while a submission's name is `Submission#N` and cannot be spelled in source.

**Decision**: a submission replaces the option value with `Submission#N` so that the chain of submissions is distinguishable in metadata and in diagnostic output. The replacement is performed by the host, which passes the generated name through the same option. A script compilation that is not a submission uses the option value unchanged.

#### The declaration model

Every top-level member of a script compilation becomes a member of the script class. The mapping is exhaustive over the four forms.

**Top-level `Dim` is a field.** The parser emits a `FieldDeclaration` for a variable declaration whose block context is a compilation unit, exactly as it does inside a class body. The field is created on the script class, so it has instance state and survives across submissions. It is not a local variable, and it is not scoped to a method body.

**Top-level `Sub` and `Function` are instance members unless declared `Shared`.** A top-level method is created on the script class with the same rules as a method declared in a class body. An instance method is a member of the same type that holds the top-level fields, so its body can read and write the top-level `Dim` variables directly, and top-level methods can call one another directly. A `Shared` method has no implicit receiver and no such access to instance state.

**Top-level types are nested types.** A `Class`, `Module`, `Structure`, `Interface`, `Enum`, or `Delegate` declaration at the top level becomes a nested type of the script class.

**Top-level executable statements form the instance initializer.** Each executable statement is recorded as an initializer entry. The synthesized initializer body is the sequence of field initializers and global statements in source order, followed by the exit label. The top-level statements do not run in the script class constructor; they run in the initializer method.

**Scope.** The scope of a top-level declaration is the top-level code of the compilation unit and the later submissions in the chain. It does not extend into the body of a nested type: inside a nested type there is no implicit receiver for the script class instance, an explicit `Me` names the nested type's own instance, and in a submission the script class name cannot be written at all.

The following script file exercises all four forms:

```vbnet
Imports System.Threading.Tasks

Class Raiser                                  ' a nested type of the script class
    Event SomethingHappened As EventHandler
    Sub Raise()
        RaiseEvent SomethingHappened(Me, EventArgs.Empty)
    End Sub
End Class

Dim counter As Integer = 0                    ' a field of the script class
Dim raiser As New Raiser

Function AddOne(value As Integer) As Integer  ' an instance member of the script class
    Return value + 1
End Function

AddHandler raiser.SomethingHappened,          ' an executable statement: part of the initializer
    Sub(sender As Object, e As EventArgs) counter += 1

Dim value = Await Task.FromResult(13)         ' top-level Await in the async initializer
Console.WriteLine(AddOne(value))              ' 14
raiser.Raise()
Console.WriteLine(counter)                    ' 1
```

#### Script classes and submission classes

A script class is one of two kinds, and the difference is observable. A **script class** proper has `DeclarationKind.Script` and `TypeKind.Class`; a **submission class** has `DeclarationKind.Submission` and `TypeKind.Submission`. Both report `IsScriptClass`. Three behaviors are dispatched by this kind and are specified separately below:

- cross-submission visibility exists only for a submission class;
- the synthesized entry point is `<Main>` for a script class and `<Factory>` for a submission class;
- `WithEvents` hookup constructors are synthesized for a non-submission script class (which follows the ordinary class path) and are not synthesized for a submission class.

**Decision**: the kind distinction is preserved rather than flattened, because a non-submission script compilation is an ordinary compilation with a synthesized container, while a submission is additionally a link in a state-preserving chain. Presenting the submission semantics as the general script semantics would be incorrect.

### Submissions

#### The submission chain

A submission is a compilation whose script compilation information carries a reference to the previous submission, the result type, and the host object type. The result type defaults to `System.Object` when the host does not specify one. A submission may consist of more than one syntax tree; the submission result is determined by the last tree.

The chain is **session-scoped**: it lives for as long as the host process holds it and is discarded when the process exits. The chain has **no bound**. **Decision**: no limit is imposed on the length of the chain, because any limit would make "state is preserved across evaluations" unpredictable; the cost of a chain link is an implementation detail and this specification makes no commitment about it.

#### Cross-submission visibility

A name lookup that fails in the current compilation searches the script class of each previous submission in turn, from the newest to the oldest, and then the host object type. The cross-submission **member lookup** covers only the members of the script classes on the chain and the members of the host object; it does not search anything else that a previous submission declared. The accumulation of `Imports` is a separate mechanism and is described under [`Imports` across submissions](#imports-across-submissions).

```text
> Imports System.Text
> Dim sb = New StringBuilder("abc")
> Console.WriteLine(sb.Length)
3
```

`sb` is a field of the first submission's script class; the second submission sees it as a member of a previous submission's script class. The lookup covers every member of a previous script class, including the nested types that top-level type declarations produce, so a type declared in one submission can be constructed and used in a later one.

**Decision**: cross-submission lookup is limited to script class members and host object members. Widening it would make a submission's private implementation details visible to unrelated later code.

#### State preservation

Each submission's script class carries the state of that submission. A reference to a member of a previous submission is bound to a reference to the corresponding previous submission instance, and the host passes the array of previous submission instances to the new submission's script class constructor. This is the mechanism that makes `sb` above refer to the same object in both submissions. The shape of the mechanism — the constructor parameter, the synthesized per-submission fields, and the name of the host object field — is an implementation detail and is not part of the contract of this specification.

### Entry points

#### The synthesized initializer

Every script class gets a synthesized initializer named `<Initialize>`. It is `Friend`, it is **asynchronous**, and its return type is `Task(Of T)`, where `T` is the submission result type or `System.Object` when no result type is specified. Its body is the instance initializer sequence described above, followed by a return statement.

The initializer is the reason top-level `Await` works: top-level statements are its body, so they execute in an async method, and `Await` is governed by the ordinary rules for async methods. There is no script-specific `Await` syntax and no relaxation of the async rules. Inside the body of a top-level `Sub` or `Function` that is not declared `Async`, the async context is that of the method itself: `Await` is not in an async context, is not parsed as an await statement, and is rejected by the ordinary rules that apply to a non-async method.

#### The script result

The value produced by a submission is called the **script result**. It is carried by the synthesized initializer's return statement, not by a `Function Main`. The rule for producing it depends on the result type:

- **Object submissions.** When the result type is `System.Object`, the value of the final expression of the submission is the script result. The final expression is implicitly converted to the result type, and a bare expression that is not the final statement of the top-level code is an error (BC31003).
- **Typed submissions.** When the result type is any other type, only an explicit `Return` statement with an expression produces the script result. A trailing expression is bound and discarded; it is not converted to the result type.

**Decision**: typed submissions follow the `Return` semantics of a `Function Main` entry point rather than the trailing-expression semantics of the C# scripting dialect. This aligns the file-execution result with C# top-level statements, where `return n` is the only source of the result, and deliberately diverges from the shared `Script<T>` scripting API, where a trailing expression is converted to the result type unconditionally. The divergence is a property of the language, not of the API: the same `Script<T>.ContinueWith` call produces a result from a trailing expression in C# and requires an explicit `Return` in Visual Basic.

A `Return` statement without an expression in the initializer produces the default value of the result type:

| Result type | Value of a bare `Return` |
|---|---|
| a type with a constant-value discriminator other than `Bad` or `Nothing`, excluding `System.String` | that type's default constant — `0` for `Integer`, `False` for `Boolean` |
| every other type, including `System.String`, `Object`, and reference types | `Nothing` |

`System.String` is called out explicitly because it is a reference type that also has a constant-value discriminator; the rule takes the `Nothing` branch for it.

#### `<Main>` and `<Factory>`

The synthesized entry point of a script class is selected by the kind of the class.

A non-submission script compilation gets `<Main>`, a `Private Shared Sub <Main>()` with no parameters. Its body creates an instance of the script class and synchronously waits for `<Initialize>`: it obtains the awaiter of the returned task and calls `GetResult` on it. `<Main>` is produced when a script file is compiled as a script compilation. A `.vbx` file that is executed as a script is a submission and therefore goes through `<Factory>`; the two paths are distinguished by whether the compilation is a submission, not by the file extension.

A submission gets `<Factory>`, a `Private Shared Function <Factory>(submissionArray As Object())` whose return type is **the initializer's return type** — that is, `Task(Of T)`, not `T`. Its body creates the submission class instance from the submission array and returns the result of calling `<Initialize>` on it. The factory body and the factory return type agree: the factory returns exactly what the initializer returns. The host consumes the factory as a delegate of type `Func(Of Object(), Task(Of T))`.

**Decision**: a submission exposes the factory rather than a synchronous `Sub` entry point, because the host must await the initializer itself and must supply the state of the previous submissions; a non-submission script compilation has no previous state and can afford to block.

**Contract status.** `<Initialize>`, `<Main>`, `<Factory>`, and `Submission#N` are compiler-generated names. They are not a public contract and cannot be referenced from source code; a program that names them does not compile. They are, however, spelled identically in the C# scripting implementation, so tooling that recognizes generated script entry points — a decompiler, a debugger, a script host — can rely on the spelling being stable across languages. The `ScriptClassName` option is different: its value is part of the compilation options, and the default value `Script` is an ordinary identifier that source code may use.

### Top-level `Await`

Top-level `Await` is available because the top-level statements are the body of the asynchronous initializer, and for no other reason. Three pieces of the model make it work, and all three are ordinary:

1. In a script, the parser treats the compilation unit as being inside an async method or lambda, so `Await` at the top level is parsed as an await statement rather than as an expression error.
2. The binder treats a field or property initializer of a script class as an async context, so an `Await` in a field initializer is accepted.
3. The initializer is asynchronous, so the await is legal at run time as well as at compile time.

The C# top-level statements design takes a different route: the synthesized entry point's signature switches between `Task`, `void`, and `int` depending on whether the top-level code uses `await` and `return`. Visual Basic instead fixes the initializer at `Task(Of T)` and performs the synchronous wait in `<Main>`, so the async surface is uniform.

### Top-level `AddHandler` and `RemoveHandler`

At the top level of a script, `AddHandler` and `RemoveHandler` are parsed as statements, not as event accessor declarations. They are therefore ordinary executable statements and become entries of the instance initializer, alongside the other top-level statements. This is a parsing decision at the compilation-unit top level of a script; inside a class body the same keywords still begin an accessor declaration.

```vbnet
Dim raiser As New Raiser
AddHandler raiser.SomethingHappened, AddressOf OnSomething   ' an ordinary statement
```

The event hookup performed by a `WithEvents` field is a separate mechanism and is dispatched by the kind of the script class, as described under [Script classes and submission classes](#script-classes-and-submission-classes).

### `Imports` across submissions

#### An explicit deviation from the `Imports` scope rule

The language specification states that the scope of an `Imports` statement "specifically does not include other `Imports` statements, nor does it include other source files" ([source-files-and-namespaces][vblang-source-files-and-namespaces]). Each submission is one source file. In the scripting dialect the imports of a submission are nevertheless in effect in every later submission of the chain: writing `Imports System.Text` once makes `StringBuilder` available for the rest of the session.

**Decision**: the deviation is deliberate and is confined to the scripting dialect. Its reason is the state-preservation requirement of the submission model; without it, the most common interactive action — import a namespace once — would not survive a single submission. Ordinary compilation is unaffected: its `Imports` scope remains file-local.

The mechanism is host-side, not compiler-side. The compiler consumes only the `GlobalImports` compilation option; the host computes that option for each submission by collecting the imports of the whole chain.

#### Normalization

The accumulated clauses are collected in a fixed order: the current submission's host-provided imports first, then the clauses of each previous submission from the oldest to the newest. Collection takes both the compilation options of a previous submission and the `Imports` clauses of its syntax trees. Textually identical clauses are removed during collection using a case-insensitive comparison, which matches the case-insensitivity of Visual Basic identifiers. The remaining clauses are then bound as project-level imports, which applies the ordinary import rules:

- **Member imports** (`Imports System.Text`) are deduplicated by the bound symbol. A clause that resolves to an import already present is ignored; the duplicate-import diagnostic is not reported for project-level imports. A `Global` qualifier is resolved while binding, so `Imports Global.System.Text` and `Imports System.Text` denote the same import and are deduplicated.
- **Alias imports** (`Imports R = System.Text`) are deduplicated by alias name, compared case-insensitively. Two clauses with the same alias name and different targets report BC30572, "Alias '{0}' is already declared"; the earlier clause wins. A project-level import diagnostic carries no source location, so the losing clause is identified by its text in the message rather than by a position.
- **XML namespace imports** (`Imports <xmlns:db="…">`) are deduplicated by prefix. Two clauses that define the same prefix report BC30573, "XML namespace prefix '{0}' is already declared"; the earlier clause wins. As with alias imports, the diagnostic carries no source location and names the clause text in its message. This is the accumulated form of the rule that an XML namespace "can only be defined once for a particular set of imports" ([source-files-and-namespaces][vblang-source-files-and-namespaces]).
- **Shadowing by the current submission.** The clauses written in the current submission's syntax tree are bound in a binder nested inside the one that holds the accumulated clauses, so an alias or XML prefix declared in the current submission's tree shadows an alias or prefix accumulated from an earlier submission of the same name without a diagnostic. The imports supplied through the current submission's options are not file-level: they join the accumulated project-level set, and a collision with a clause from an earlier submission is therefore reported rather than shadowed. Member imports have no shadowing behavior; they simply accumulate.

**Decision**: a collision between two accumulated clauses is reported as a diagnostic rather than resolved silently, and the earlier clause wins. A collision between an accumulated clause and a clause written in the current submission's syntax tree is resolved in favor of the current submission, because the author of the current submission can see and change it.

**Failure.** A parse failure of an accumulated clause is reported as a diagnostic and is not propagated as an exception. **Decision**: an accumulated clause is untrusted input — it was written in an earlier submission and is replayed by the host — so the accumulation path treats a malformed clause as a diagnostic condition rather than as a fatal error.

### Compilation options

The scripting dialect selects its compilation options by default:

| Option | Value | Effect |
|---|---|---|
| `Option Strict` | `Off` | Permissive conversions and late binding are allowed; the late-bound, loosely typed behavior of the language is available. |
| `Option Infer` | `On` | `Dim x = …` infers a type instead of typing the variable as `Object`. |
| `Option Explicit` | `On` | Every variable must be declared before use. |

**Decision**: these defaults are the concrete carrier of the dialect's character. `Option Strict Off` is the switch that enables late binding; `Option Infer On` makes inference usable in a top-level declaration; `Option Explicit On` keeps the declaration requirement that makes a top-level `Dim` meaningful. The combination is what allows a script to write `Dim value = Await Task.FromResult(13)` and use `value` as an `Integer` in the next statement.

### Script-specific restrictions and diagnostics

The scripting dialect introduces a small family of diagnostics. They are layered: `Namespace` is reported while parsing; the remaining restrictions are reported while binding.

| Code | Diagnostic | Condition | C# counterpart |
|---|---|---|---|
| BC36965 | `ERR_NamespaceNotAllowedInScript` | A `Namespace` declaration in a script. The declaration is still processed by the declaration table so that later errors are reported more accurately. | CS7021, same name and meaning |
| BC36966 | `ERR_KeywordNotAllowedInScript` | An **explicit** `Me`, `MyBase`, or `MyClass` in a script class; a `Return` in top-level script code that is not in the script initializer; a `Yield` in top-level script code. Implicit `Me` references are allowed. | none for `Me`/`MyBase`/`MyClass` — the C# scripting dialect lets `this` name the submission instance; `Yield` corresponds to CS7020 |
| BC31003 | `ERR_UnexpectedExpressionStatement` | A bare expression statement that is not the final statement of the compilation unit's top-level code. | the analogous general diagnostic is CS0201; the C# scripting dialect has no separate diagnostic for a non-final expression |
| BC30545 | `ERR_PropertyAccessIgnored` | A property access or late-bound property group used as a statement. The diagnostic is suppressed for the final statement of the compilation unit's top-level code. | none |
| BC36964 | `ERR_ReferenceDirectiveOnlyAllowedInScripts` | `#R` in ordinary compilation. | CS7011 |
| BC36967 | `ERR_LoadDirectiveOnlyAllowedInScripts` | `#Load` in ordinary compilation. | CS8097 |
| BC42367 | `WRN_MainIgnored` | A `Main` entry point is ignored because the compilation has a script class: global code is the entry point. Reported as a warning, not silently. | CS7022, same name and meaning |

The directive diagnostics above are mode gates only; the semantics of the directives themselves are specified separately, as is the `#!` shebang directive.

**The `Me` restriction.** The condition for BC36966 on `Me`, `MyBase`, and `MyClass` is that the containing type is a script class, and it applies to every executable statement in that type. It therefore covers the **entire body of a top-level `Sub` or `Function`**, not only the top-level statements, and it covers the top-level statements as well. Implicit references are permitted, so a top-level instance method can call another top-level method and read a top-level field without qualification; only the explicit keyword is rejected.

```vbnet
Dim counter As Integer = 0

Function NextCounter() As Integer
    Dim counter As Integer = 5
    Return counter                ' Okay: the local shadows the field
End Function

Function ReadCounter() As Integer
    Return Me.counter             ' Error BC36966: explicit 'Me' is not allowed
End Function
```

The restriction has a portability consequence for code moved from the C# scripting dialect: `this.X`, which is the ordinary way to reach a previous submission's state in `.csx`, must be rewritten as an unqualified reference, because the explicit `Me` is rejected. Implicit access to the same state is unaffected.

**The `Return` restriction.** A `Return` statement is rejected with BC36966 only when it is in top-level script code and is not in the script initializer. A `Return` in the top-level statements is legal — it is the initializer's return statement, and it produces the script result. The condition is not the same as the condition for `Me`: a `Return` inside the body of a top-level `Sub` or `Function` is the ordinary return of that method, and a `Return` in a nested type's method is likewise an ordinary return.

**The `Yield` restriction.** A `Yield` in top-level script code is rejected. A script initializer is not an iterator, and top-level statements have no iterator context.

#### Top-level labels and `GoTo`

A `LabelStatement` at the top level is not added to the instance initializer sequence, so a top-level label does not itself contribute a statement to the initializer body. A top-level `GoTo` is an ordinary executable statement and is part of the initializer sequence; it binds to a top-level label because labels are collected for the whole compilation unit rather than from the initializer sequence, so no diagnostic is reported. Because the label statement is not emitted into the initializer body, the runtime effect of the jump is likewise not guaranteed.

**Decision**: the behavior of a top-level `GoTo` that targets a top-level label is outside the guarantees of this specification. Scripts that need control flow express it with the ordinary block statements, which are fully supported at the top level.

## Soundness
[soundness]: #soundness

The model is a containerization, not a new declaration system. Every top-level form keeps its ordinary spelling and its ordinary meaning; the only change is the container the declaration lands in, and the container is synthesized rather than written. The four mappings are exhaustive over the declarations a compilation unit can contain, so no declaration is left without a home.

The places where the dialect deliberately departs from the ordinary language are explicit in this specification. The `Imports` scope deviation is confined to the scripting dialect and exists to make the submission model usable; ordinary compilation keeps the file-local scope. The compilation-option defaults change the typing behavior of scripts, not the meaning of any construct.

Most of the restrictions listed above are confined to constructs that have no ordinary-language meaning in a top-level script: a `Namespace` declaration has nothing to be nested in, and a `Yield` in top-level statements has no iterator. The explicit `Me` restriction is the one case in which a construct that is meaningful inside an ordinary method body is rejected, and it is a deliberate restriction rather than a change of meaning: the implicit reference to the same state remains available, so no program loses access to anything it could otherwise reach. A program that is legal both as a script and as ordinary code has the same meaning in both; the differences are the placement of declarations, the synthesized entry point, and the result rule for typed submissions.

## Drawbacks
[drawbacks]: #drawbacks

- **Every submission is a full compilation.** A continuation does not reuse the previous compilation incrementally; the chain is walked on each lookup, and the cost of a session grows with its length. The model trades compilation cost for state preservation.
- **Synthesized containers obscure diagnostics.** A top-level declaration lands in a `Friend NotInheritable` synthesized class whose name (`Submission#N`) is not writable in source. Type names and stack frames are less recognizable than they would be in a hand-written module, and an error in top-level code has a less familiar context than an error in a class body.
- **The dialect differs from the ordinary language.** A top-level `Return` is legal, a `Namespace` is not, an explicit `Me` is not, and a bare expression is legal at the end of a submission. Each difference raises the cost of moving code between a script and ordinary code, which is the same concern the C# design notes raised when they warned that a scripting dialect can become "a third dialect" of the language ([LDM-2020-02-26][ldm-2020-02-26]).
- **The `Imports` accumulation is host-side.** The host must collect and re-apply the clauses of the chain, and it must stay in step with the compiler's import semantics as new clause forms are added. This is a long-lived coupling between the host and the language.
- **Explicit `Me` is unavailable where a top-level method body would otherwise want it.** A local declaration that shadows a top-level field cannot be bypassed with `Me.field` inside a top-level method, although implicit access to the field is allowed.

## Alternatives
[alternatives]: #alternatives

- **Require an explicit `Module` wrapper.** This is the ordinary-language form. It gives up the property that a declaration typed at the top level is immediately usable, which is the entire point of the scripting scenario.
- **Rewrite the source text in the host, wrapping the top level in a `Module`.** Rejected: it shifts every diagnostic line by the number of inserted lines, duplicates the parser's and binder's decisions in the host, and hides the real tree from editors and language servers.
- **Add syntax for top-level `Await`.** Unnecessary. The initializer is already an async method, so `Await` is governed by the ordinary async rules; a new form would create a second way to express an existing construct.
- **Let a trailing expression produce the result of a typed submission.** Rejected. It would conflict with the `Return` semantics of a `Function Main` entry point and would diverge from C# top-level statements, where a trailing expression is not a source of the result.
- **Accumulate `Imports` in the compiler.** Rejected. The host already owns the chain and the per-submission import options; keeping the compiler to a single responsibility — consume `GlobalImports` — avoids a second, compiler-internal notion of accumulated imports.
- **Give a submission a synchronous `Sub` entry point like a script file.** Rejected. The host must supply the previous submissions' state and must await the initializer itself; a blocking entry point would force the host to block on a task it is responsible for scheduling.

### The C# baseline

Visual Basic and C# answer the same questions differently in three separate places, and the answers must not be conflated.

- **Declaration and state model** — the C# **scripting dialect** (`.csx`, the shared `Script<T>` API). It is the closest counterpart: it also synthesizes a submission class, also keeps top-level state across submissions, and also chains submissions. This specification's model of declarations, visibility, and state is the Visual Basic counterpart of that model.
- **File-execution result** — C# **top-level statements**. There, the top-level code is semantically a `static async Task Main`, and an explicit `return n` is the source of the result. The typed-submission rule above follows this face.
- **Interactive printing** — C# **interactive** (`csi`), where a trailing expression is printed. Printing behavior is specified separately and does not affect the result rule.

The following table compares the declaration and state model with the C# scripting dialect and, where they differ, states the difference. The comparison is symmetric: each row states what the construct is in both languages.

| Dimension | C# scripting dialect (`.csx`) | Visual Basic scripting dialect |
|---|---|---|
| Top-level variable | a field of the submission class, preserved across submissions | a field of the script class, preserved across submissions |
| Top-level method | an instance member of the submission class, or a `static` member if declared `static` | an instance member of the script class, or a `Shared` member if declared `Shared` |
| Access to a previous submission's state | `this.X` from a top-level member | an unqualified reference; the explicit `Me` is rejected (BC36966) |
| Entry point | a submission factory consumed by the host | `<Factory>` for a submission, `<Main>` for a script file |
| Trailing expression | converted to the submission result type unconditionally | converted only for an Object submission |
| Top-level `Imports` | accumulate across submissions | accumulate across submissions (an explicit deviation from the `Imports` scope rule) |

The rows for access to a previous submission's state and for the trailing expression are the deliberate divergences from the C# scripting dialect: the result rule follows C# top-level statements instead, and the `Me` restriction is Visual Basic's own. The `Imports` row is a divergence from the ordinary Visual Basic `Imports` scope rule rather than from C#; the C# scripting dialect accumulates imports in the same way.

## Unresolved questions
[unresolved]: #unresolved-questions

None.

## Considerations
[considerations]: #considerations

### Ahead-of-time compilation and trimming

A submission is a script-runtime construct: it is compiled to an in-memory assembly, loaded dynamically, and executed against state that is passed as an object array. It is outside the scope of ahead-of-time compilation and trimming. The ahead-of-time path covers ordinary compiled output only; a `.vbx` submission is never part of an ahead-of-time product.

### Tooling: accumulated `Imports` are not in the syntax tree

The accumulated imports of a session are carried in the **compilation options** of each submission, not in its syntax tree. The `Imports` clauses of a submission's own syntax tree are exactly the clauses the author wrote in that submission; the clauses accumulated from earlier submissions are not copied into the tree. A tool that wants to answer "which imports are in effect for this submission" — an editor's completion list, a refactoring, a language server — must read the compilation options, not the tree. Reading only the tree silently omits every accumulated import.

### Two different questions about a submission result

Two distinct queries must not be conflated. The rule that produces the script result is the result-type rule described above; it is a property of binding and rewriting, and it is expressed in terms of the result type. A separate query answers whether a submission has a printable result for the host — a question the host asks before deciding whether to print anything. That query examines the shape of the final statement of the last tree and does not consider the result type, so for a typed submission it can answer differently from the result rule. The two questions have different purposes: one defines the language, the other drives the host's printing decision.

### Late-bound property access at the end of a submission

Under `Option Strict Off`, a trailing late-bound member access on an `Object` receiver binds as an invocation rather than as a value, so it reports the error for a discarded value rather than being printed. This is existing behavior and is not part of the result rule.

### Boundaries of the dialect

The following items follow from the implementation and are stated rather than left implicit:

- **Top-level labels and `GoTo`.** As specified above, a top-level label is not part of the initializer sequence and is not emitted into the initializer body; a top-level `GoTo` is part of the sequence. The runtime effect of the jump is outside the guarantees of this specification.
- **`WithEvents` in a submission class.** The synthesis of `WithEvents` hookup constructors is dispatched by the kind of the script class. A non-submission script class follows the ordinary class path; a submission class synthesizes no hookup constructors. A `Handles` clause in a submission class is not supported: no diagnostic is reported for it, and the compilation does not complete. This boundary is stated, not specified.
- **Alias and XML-prefix collisions between accumulated clauses.** The rules above define which clause wins and which diagnostic is reported. A project-level import diagnostic carries no source location, so the losing clause is identified only by its text in the message; it cannot be located by line number, and a losing clause written in an earlier submission is no exception.

## Testing
[testing]: #testing

The dialect is exercised by in-memory compilation and execution tests that have no side effects: they do not perform network access, file writes outside an isolated temporary directory, process launches, or registry access. Script-file and interactive cases run through the script host with a captured console; compiler-level cases compile in memory from a script-mode parse option.

- **Declaration-model and session tests.** A top-level `Dim` declared in one submission is readable in the next. A top-level `Function`, `Class`, `Module`, and `Delegate` declared in one submission are each usable in a later submission: an instance of the type is constructed with an object initializer, the address of the function is taken, and a member of the module is called. An import written in one submission is in effect in the next and does not replace imports supplied through the host's options.
- **Entry-point and result tests.** A typed script ignores a trailing expression, and a typed script's bare `Return` yields the default value; an explicit top-level `Return` value is the result and takes precedence over a trailing expression; top-level executable statements run in source order. In file execution, a trailing expression and a `?` statement do not set the exit code, an explicit `Return` does, and a bare `Return` yields zero.
- **Async and event tests.** A top-level `Await` in a script file, a bare `Await` statement, and an `Await` whose value is the result all compile and run. A top-level `AddHandler` with a lambda, a top-level `AddHandler` whose handler was declared in a previous submission, and a top-level `RemoveHandler` compile and take effect.
- **Restriction tests.** In script mode, a `Namespace` declaration reports BC36965, and an explicit `Me`, `MyBase`, or `MyClass` reports BC36966 — including when it appears inside the body of a top-level `Sub`. A top-level `GoTo` and its label compile without errors. A top-level `On Error Resume Next` and a top-level `RaiseEvent` report their unsupported-statement diagnostics.
- **Statement-form tests.** A bare expression that is not the final statement of the top-level code reports BC31003, and a member access in the same position reports BC30545; in file execution both forms are accepted and ignored. A bare expression inside a nested `Sub` still reports its ordinary diagnostic, and a trailing late-bound member access on an `Object` receiver is not printed.
- **Session-isolation tests.** A failed submission does not change the state of the session.

## Related Items
[related]: #related-items

- [C# 9.0 top-level statements](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-9.0/top-level-statements.md) — the C# counterpart of the file-execution model
- [LDM-2020-01-22](https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-01-22.md) — scripting and interactive scenarios, submission state preservation
- [LDM-2020-02-26](https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-02-26.md) — the risk of a scripting dialect becoming a third dialect
- [LDM-2020-03-09](https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-03-09.md) — top-level statements treated as being inside an async entry point
- [LDM-2020-04-15](https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-04-15.md) — trailing expressions are not a result in the language proper
- [Visual Basic design notes, 2017-12-06](https://github.com/dotnet/vblang/blob/main/meetings/2017/vbldm-notes-2017.12.06.md) — issue #102, top-level statements in a single entry-point file
- [Source Files and Namespaces](https://github.com/dotnet/vblang/blob/main/spec/source-files-and-namespaces.md) — the `Imports` scope rule and the entry-point rule
- [Statements](https://github.com/dotnet/vblang/blob/main/spec/statements.md) — local declarations and the `Return` statement rule
- [Type Members](https://github.com/dotnet/vblang/blob/main/spec/type-members.md) — `Dim` as a variable member modifier, `WithEvents`, extension methods
- [Types](https://github.com/dotnet/vblang/blob/main/spec/types.md) — standard modules: implicitly `Shared` members, never instantiable
- [Script<T>](https://github.com/dotnet/roslyn/blob/main/src/Scripting/Core/Script.cs) — the shared scripting API

[ldm-2020-01-22]: https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-01-22.md
[ldm-2020-02-26]: https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-02-26.md
[vbldm-2017-12-06]: https://github.com/dotnet/vblang/blob/main/meetings/2017/vbldm-notes-2017.12.06.md
[vblang-source-files-and-namespaces]: https://github.com/dotnet/vblang/blob/main/spec/source-files-and-namespaces.md
[vblang-statements]: https://github.com/dotnet/vblang/blob/main/spec/statements.md
[vblang-type-members]: https://github.com/dotnet/vblang/blob/main/spec/type-members.md
[vblang-types]: https://github.com/dotnet/vblang/blob/main/spec/types.md
