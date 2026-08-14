# Optional Question Mark Prefix for REPL Expressions

* [x] Proposed
* [x] Prototype: Complete
* [x] Implementation: Complete
* [x] Specification: [Complete](spec-optional-question-prefix.md)

## Summary
[summary]: #summary

This specification makes the leading question mark that denotes a printing expression in the interactive window optional for value references. When the final statement of a submission is a bare expression — one that does not begin with `?` — the compiler classifies the expression by the shape of its bound result. An expression that binds to a value reference — a local variable, a field, a parameter, a property access, or a late-bound property group — is evaluated and its value is printed, exactly as though it had been written with a leading `?`. An expression that binds to a method group or to an invocation keeps its existing statement semantics: a parenthesized-less `Sub` call remains a call, and a side-effecting invocation keeps its side effect.

The feature preserves the meaning of every statement that is legal today. Assignments, declarations, and invocations are unchanged; only a bare value reference, which previously failed to compile, now prints its value. The behavior is confined to the interactive window. A trailing bare expression in a script file is accepted and ignored: it is not printed and it does not set the process exit code, which remains determined solely by the entry point.

The C# interactive window already prints a bare expression without a prefix, and the C# scripting dialect is the only C# dialect in which a trailing expression produces a result; regular C# deliberately keeps a distance from its scripting dialect, as recorded in [LDM-2020-04-15][ldm-2020-04-15]. This feature adopts the same compiler-level treatment for Visual Basic.

## Motivation
[motivation]: #motivation

In the interactive window today, evaluating an expression requires the `?` prefix. `? Now` prints the value of `Now`; `Now` alone produces the compile-time error BC30545, "property access must assign to the property or use its value." For interactive evaluation this error is noise. The user has typed an expression and wants to see its value; the compiler instead demands that the value be assigned or consumed.

The C# interactive window has no such requirement. Typing `DateTime.Now` evaluates the expression and prints the value with no prefix. This is the most visible interaction gap between the two interactive windows, and it is the first error a new user is likely to meet: users moving from the C# interactive window, PowerShell, or a notebook habitually type an expression bare and receive a compiler error that says nothing about what to do next.

The expected outcome is that "type an expression, get its value" becomes the intuitive behavior of the Visual Basic interactive window. The `?` prefix remains available for users who prefer it, and its meaning is unchanged. Because the feature changes only statements that previously failed to compile, no existing session behaves differently.

## Detailed design
[design]: #detailed-design

### The `?` print statement

The interactive window recognizes a legacy print statement: a statement whose first token is a question mark. The `?` denotes an expression to be evaluated and printed by the host. The expression is bound as an R-value, the statement is a print statement, and a print statement always yields a submission result, which the host prints through its value formatter.

```vbnet
> ? Now
' 2026-08-14 12:34:56
> ? 1 + 2
' 3
```

The print statement is exclusive to the interactive window. In ordinary compilation a leading question mark is not a valid statement start and is reported as an unexpected expression statement (BC31003). This feature does not change the print statement.

### Classification of a bare trailing expression

A submission that does not begin with `?` is parsed as ordinary top-level script code. When the final statement of the submission is an expression statement — a bare expression — the compiler binds the expression and classifies the bound result:

- **Value-producing shapes.** An expression that binds to a value reference — a local variable, a field, a parameter, a property access, or a late-bound property group — or to a computed value, such as a literal or a binary expression, is evaluated and its value becomes the submission result, which the host prints. For a property access or a late-bound property group, the diagnostic that reports an unused property access is suppressed for the final statement and the expression is bound as an R-value.
- **Invocation-shaped shapes.** An expression that binds to a method group or to an invocation is treated as a call statement. A method group is reclassified as an argumentless invocation. A `Void` invocation remains a legal statement and is not printed. A non-`Void` invocation produces a submission result and prints, exactly as it does today.

The following table summarizes the observable behavior at the end of a submission. The classification is anchored to the shape of the bound result, not to the text of the expression and not to a list of error codes.

| Submission | Bound shape | Behavior at the end of a submission |
|---|---|---|
| `Now` | property access | printed |
| `DateTime.Now` | property access | printed |
| `before` | local variable | printed |
| `1 + 2` | expression value | printed: `3` |
| `x > 5` | expression value | printed: `False` |
| `"a" & "b"` | expression value | printed: `"ab"` |
| `MySub` | method group, `Sub` → argumentless call | legal statement, not printed |
| `Console.WriteLine("hi")` | invocation, `Void` | legal statement, not printed |
| `SomeFunction()` | invocation, non-`Void` | legal statement; prints as today |
| `x = 5` | assignment | legal statement, not printed |

### Value references

A value reference is a bound expression whose value is a value: a local variable, a field, a parameter, a property access, or a late-bound invocation whose member group is a property group. These shapes are evaluated and printed when they appear as the final statement of a submission.

For a property access and a late-bound property group, the error that reports an unused property access (BC30545) is suppressed for the final statement and the expression is bound as an R-value. The remaining shapes bind without a diagnostic.

```vbnet
> Now
' 2026-08-14 12:34:56
> DateTime.Now
' 2026-08-14 12:34:56
```

### Computed expressions

A literal or a binary expression at the start of a statement was previously mis-parsed: a leading numeric literal such as `1 + 2` was parsed as a label, and a leading identifier with a trailing binary operator such as `x > 5` was mis-parsed as an invocation. At the top level of a script, these are now parsed as expression statements. They bind as ordinary values, produce no "value discarded" diagnostic, and print at the end of a submission.

```vbnet
> 1 + 2
' 3
> x > 5
' False
> "a" & "b"
' "ab"
```

### Invocations and method groups

An invocation binds to a bound invocation or, for a late-bound member access on an `Object` receiver, to a late-bound member access. A method group is a reference to one or more methods, not to a value; at the start of a statement it is reclassified as an argumentless invocation.

None of these shapes is newly printed by this feature. A `Void` invocation — a parenthesized-less `Sub` call or a side-effecting `Sub` call — remains a legal statement and produces no submission result. A non-`Void` invocation remains a legal statement that produces a submission result and prints, exactly as it does today.

```vbnet
> MySub
' the Sub runs; nothing is printed
> Console.WriteLine("hi")
' the line is written; nothing else is printed
```

The reclassification of a method group is what preserves the parenthesized-less `Sub` call: typing the name of a `Sub` at the interactive window calls it and does not print, which is the behavior a Visual Basic user expects. A late-bound member access on an `Object` receiver binds as an invocation and is not printed; the interactive window currently reports BC30491 for a trailing late-bound member access because the submission-result path treats the access as a value. This is pre-existing behavior and is not part of this feature.

### Bare variable references

A bare variable reference is a value reference and is printed. After

```vbnet
> Dim before = Now
```

typing `before` prints the stored value:

```vbnet
> before
' 2026-08-14 12:34:56
```

This aligns the interactive window with the C# interactive window, where typing a variable name prints its value. A bare identifier is syntactically ambiguous between a variable reference and a method name; the two are distinguished at binding time, when the identifier binds either to a value reference or to a method group.

### Parsing a leading bare expression

In top-level script code, a leading bare expression is parsed as an expression statement. Two ambiguities are resolved at parse time:

- A bare numeric literal followed by a colon is a label (`1:`); a bare numeric expression is an expression statement (`1 + 2`). A leading literal such as `True`, `Nothing`, or a string is likewise parsed as an expression statement.
- A bare identifier is parsed as an identifier expression, so that a variable reference and a method name can be distinguished at binding time. A member access such as `DateTime.Now` or `obj.MySub` keeps its invocation shape, which preserves the parenthesized-less member `Sub` call (`obj.MySub`) as a legal statement.

The behavior is gated on top-level script code. Ordinary compilation, and code inside a nested method of a script file, parse and bind as before: in ordinary compilation a leading `1 + 2` is reported as a label (BC30801) and a bare property access inside a method reports the unused-property-access error (BC30545).

### Only the final statement produces a result

Consistent with the C# scripting dialect, only the final statement of a submission produces a result. A bare expression that is not the final statement is still an error; it is reported as an unexpected expression statement (BC31003) and the submission does not run.

```vbnet
> 1 + 2 : x = 5
' Error BC31003: the bare expression is not the final statement
```

A member access that is not the final statement continues to report the unused-property-access error (BC30545), because the suppression applies only to the final statement. Each interactive submission is compiled as an independent compilation unit, so "final statement" is determined per submission. A later submission that continues the session is a fresh set of statements, and its own final statement is eligible. The final statement of a submission is the same statement that the submission-result mechanism considers final, so the two determinations agree by construction.

### Script mode and the exit code

The printing behavior is confined to the interactive window. The scripting source-code kind serves both interactive submissions and script files, so the parser and binder accept a bare expression at the end of a script file. A trailing expression in a script file is not printed, and it does not set the process exit code.

The exit code of a script is determined solely by the entry point: an explicit `Return value` sets it, and a bare `Return` or falling off the end sets it to 0. A trailing expression is never a source of the exit code. This is orthogonal to the exit-code semantics of a `Function Main` entry point in ordinary compilation; in both modes a trailing expression is not a source of the exit code.

## Soundness
[soundness]: #soundness

The invariant preserved by this feature is that no statement that compiles today changes meaning. The only statements whose behavior changes are a subset of statements that previously failed to compile, and each of those changes from a compile-time error to a printed value.

The statements that are legal in the interactive window today are assignments, declarations, control-flow statements, and invocations. Assignments, declarations, and control-flow statements are not expression statements and are never classified by this feature. An invocation binds to a bound invocation or, for a parenthesized-less `Sub` call, is reclassified from a method group to a bound invocation; this feature does not alter those shapes. A `Void` invocation therefore remains a legal statement that is not printed, and a non-`Void` invocation keeps its existing behavior of producing a submission result. Neither changes.

The statements that previously failed to compile are exactly the bare value references and computed expressions that the interactive window rejected: a property access used as a statement (BC30545), a leading bare expression that the parser mis-shaped — a leading literal such as `1 + 2` parsed as a label, or a leading identifier with a trailing binary operator such as `x > 5` mis-parsed as an invocation — and a bare variable reference that the parser wrapped into an invocation. For each of these, the new behavior binds the expression as an R-value and prints the value, which is precisely the behavior the user could obtain by writing the same expression with a leading `?`. The feature introduces no new evaluation semantics; it makes an existing explicit operation implicit.

The explicit `?` prefix is unchanged, so every behavior that is reachable after this feature was reachable before it. The only difference is that a subset of the previous errors is now silently evaluated. Because that subset previously produced no program at all — the submission failed to compile and did not run — no previously observable behavior is removed.

## Drawbacks
[drawbacks]: #drawbacks

- **Silent printing.** A bare expression that previously failed to compile now prints a value. If the user intended a statement — for example, meant to invoke a `Sub` but typed a property name — the compile-time error is replaced by a printed value, and the mistake may go unnoticed. In the interactive window, the printed value is itself feedback, but it is weaker than an error for revealing intent mistakes.

- **Dialect divergence.** A bare expression is not a legal statement in ordinary Visual Basic. Making it print in the interactive window increases the difference between the interactive dialect and the ordinary language, as the C# interactive window already differs from C#. The explicit `?` remains available, but users who learn the interactive window first must unlearn the bare expression when they write ordinary code.

- **Script-mode error surface.** A trailing bare expression in a script file changes from a compile-time error to an accepted, ignored statement. Scripts or build-time checks that relied on the error must be updated. This is accepted by analogy with the C# decision to leave a distance between C# and its scripting dialect.

- **Classification maintenance.** The classification of bound-result shapes must be kept in sync as the language adds new expression shapes, so that new value references print and new statement shapes are not misclassified. The classification is a property of the binding model, not a list of error codes, which keeps the surface small.

## Alternatives
[alternatives]: #alternatives

- **Keep the `?` mandatory.** No behavior changes, but the BC30545 noise and the gap with the C# interactive window remain.

- **Improve the error message.** Keep `?` mandatory and change the unused-property-access error to suggest `?`. Lower risk, but does not make "type an expression, get a value" intuitive.

- **Print only the final expression of a continuation.** Auto-print only after a line-continuation submission. Narrower scope, but diverges from single-line behavior and from the C# interactive window.

- **Print every expression statement.** Auto-print all bare expressions, including side-effecting invocations. This was considered and rejected: it expands the silent-behavior surface to every statement and would print the results of `Sub` calls.

- **Add a new print keyword.** Introduce a more explicit printing form. This reintroduces the cognitive burden the feature removes.

- **Rewrite the submitted text in the host.** Insert a leading `?` before running the submission. This was set aside: the compiler-level treatment keeps a single binding model, keeps IntelliSense and error recovery consistent with the parsed tree, and does not depend on the host recognizing which statements are expressions.

## Unresolved questions
[unresolved]: #unresolved-questions

None.

## Considerations
[considerations]: #considerations

### Alignment with the C# interactive window

The C# interactive window prints a bare expression with no prefix. In C#, the ability to end with an expression that produces a result is confined to the scripting dialect and is meaningful mainly in an interactive setting; regular C# deliberately does not allow a trailing expression to produce a result, and the C# language design notes accepted the remaining distance between C# and its scripting dialect ([LDM-2020-04-15][ldm-2020-04-15]). This feature adopts the same compiler-level setting for Visual Basic: the trailing expression of an interactive submission is bound to produce a result, and the host prints the returned value through the same path used by `?`. No host-side text rewriting is involved.

### The interactive dialect

The interactive window is a distinct dialect of Visual Basic, as C# scripting is a distinct dialect of C#. The C# design notes have repeatedly flagged the risk that an expanding scripting dialect fractures the language ([LDM-2019-09-11][ldm-2019-09-11], [LDM-2020-02-26][ldm-2020-02-26]) and that the scripting submission system preserves state across evaluations ([LDM-2020-01-22][ldm-2020-01-22]). This feature stays within the interactive window and keeps the explicit `?` for users who prefer it, so the difference between the interactive dialect and the ordinary language is limited to making an existing explicit operation optional. The submission model itself is unchanged: each submission is an independent compilation, and state is preserved across submissions as it is today.

### Script-mode error surface

Because the scripting source-code kind serves both interactive submissions and script files, the parser and binder cannot tell the two apart. A trailing bare expression in a script file is therefore accepted and ignored rather than reported as an error. This is accepted by analogy with the C# acceptance of the distance between C# and its scripting dialect. The change is limited to the error surface: the exit code of a script is unaffected, and a bare expression inside a nested method of a script file still reports the original error.

### Option Strict

The classification and the printing path are the same under `Option Strict On` and `Option Strict Off`. A bare expression used as a statement has no target-type context, so the implicit conversions of the loose-typing mode cannot apply to it. The printing path performs no conversion and requires only that the value is not `Void`. A late-bound member access on an `Object` receiver binds as an invocation and is therefore not printed under either setting.

### `?` and the relational operator

Within a print statement, the expression context is the ordinary expression context, in which `=` is the relational equality operator. `? x = 5` therefore prints the comparison result, and an assignment is written as a statement, `x = 5`. The two forms are distinguished at parse time and do not conflict.

```vbnet
> ? x = 5
' False
```

## Testing
[testing]: #testing

The feature is exercised by in-memory tests that have no side effects: they do not perform network access, file writes, process launches, or registry access. Interactive cases run through an in-memory runner with a captured console; script cases write only to the existing isolated-temporary-directory harness.

- **Parser tests.** Script-top-level bare expressions parse as expression statements; `1:` remains a label; a bare identifier parses as an identifier expression; a member access keeps its invocation shape; ordinary compilation is unaffected.

- **Semantic tests.** Value references bind without a diagnostic and produce a submission result; method groups and `Void` invocations bind as statements; a bare expression that is not the final statement reports BC31003; a member access that is not the final statement still reports BC30545; the behavior is the same under `Option Strict On` and `Option Strict Off`.

- **Compilation API tests.** A matrix of submissions asserts whether each produces a submission result: `1`, `Now`, `DateTime.Now`, `1 + 2`, and `"a" & "b"` produce one; `x = 5`, `Dim i As Integer`, `Imports System`, `Console.WriteLine()`, and `Sub Goo() End Sub` do not; `?1` and `?Console.WriteLine()` continue to produce one.

- **End-to-end interactive tests.** The in-memory runner asserts printed value lines and the absence of error output for `Now`, `1 + 2`, `"a" & "b"`, a bare comparison, and a bare variable reference; asserts no value output for `x = 5`, `Console.WriteLine("hi")`, a parenthesized-less `Sub` call, and a compound assignment; asserts `? Now` and `? (1 + 2)` are unchanged; asserts malformed input (`Now x`) still reports an error; and asserts that a bare expression that is not the final statement reports BC31003. The final statement of a multi-line continuation prints consistently with a single-line submission.

- **Script-mode tests.** A script file containing `1 + 2` or `Now` exits with code 0 and produces no output; `? 21` still exits 0; `Return 21` still exits 21; a bare `Return` still exits 0; and a bare expression inside a nested method of a script file still reports the original error.

- **Ordinary-compilation tests.** `1 + 2` inside a method still reports the label error (BC30801); `? 1` still reports BC31003; a bare property access inside a method still reports BC30545; and a parenthesized-less `Sub` call inside a method remains legal.

## Related Items
[related]: #related-items

- [LDM-2020-04-15](https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-04-15.md) — decisions on top-level statements and on expressions at the end
- [LDM-2020-01-22](https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-01-22.md) — scripting and interactive, submission state
- [LDM-2019-09-11](https://github.com/dotnet/csharplang/blob/main/meetings/2019/LDM-2019-09-11.md) — the scripting dialect and the risk of a third dialect
- [LDM-2020-02-26](https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-02-26.md) — the scripting dialect and the risk of a third dialect
- [Top-level statements](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-9.0/top-level-statements.md) — the C# proposal discussed in LDM-2020-04-15

[ldm-2020-04-15]: https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-04-15.md
[ldm-2020-01-22]: https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-01-22.md
[ldm-2019-09-11]: https://github.com/dotnet/csharplang/blob/main/meetings/2019/LDM-2019-09-11.md
[ldm-2020-02-26]: https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-02-26.md
