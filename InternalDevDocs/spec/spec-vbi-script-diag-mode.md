# Diagnostics-Only Script Checking (`/check`)

* [x] Proposed
* [x] Prototype: Complete
* [x] Implementation: Complete
* [x] Specification: [Complete](spec-vbi-script-diag-mode.md)

## Summary
[summary]: #summary

This specification adds a diagnostics-only checking mode to the Visual Basic script host. The `/check` switch compiles a `.vbx` script file as a single submission, reports the resulting diagnostics, and exits — without executing the script and without writing any output file. The switch is a compile gate: exit code `0` means the script has no compilation errors (warnings are allowed), and exit code `1` means it has at least one error.

Before this feature, the one-shot script path compiled and executed a script in a single step, so a successful run swallowed warnings entirely and there was no way to obtain diagnostics without running the script. The interactive window already gated each submission on a compile-without-run step, but that path is per-submission, truncates the diagnostic display, and is not a one-shot file check. `/check` connects the existing compile-without-run mechanism to the one-shot script-file path and makes it the observable contract.

The mode is analogous to `cargo check` and `tsc --noEmit`: a full semantic check with no artifact and no execution. It is intended for script-authoring loops and for AI toolchains that need a diagnostics-only feedback channel without taking on script side effects.

## Motivation
[motivation]: #motivation

A script file is executed by compiling it and running it in one step. A successful run prints its output and exits `0`, and warnings — an unused local, a late-bound call — are never shown. A failing run stops at the first compilation error, so the only way to see diagnostics is to trigger them by running. Three existing paths fail the "diagnostics without side effects" need, for three different reasons:

| Path | Behavior | Gap |
|---|---|---|
| **Script execution** (`vbi script.vbx`) | compiles and runs in one step | a successful run reports no warnings; there is no way to check without running |
| **Interactive mode** (`/i`) | checks each submission, then runs it | per-submission only, and the interactive diagnostic display is truncated |
| **Compile mode** (a `.vb` source, or `/out:`/`/target:`) | runs a full ordinary compilation and emits an assembly | emits a file; it is not "diagnostics only" |

The mechanism this feature needs already exists in the script engine. `Script.Compile()` compiles a submission and returns its diagnostics without running it; it is the same API the interactive window uses to decide whether each submission may run. The one-shot script path simply never calls it — it goes straight to `RunAsync`, welding compilation and execution together. What is missing is a switch, not a mechanism.

The primary consumer is an AI toolchain iterating on `.vbx` scripts, which needs a feedback channel that returns compiler diagnostics without executing script code. The naming of the switch therefore follows a "eliminate AI misreading" criterion: an AI interprets a bare switch from its training-data prior, before reading any help text. `/check` has a clean, matching prior — `cargo check`, `node --check`, and `biome check` all mean "verify, produce no artifact, do not execute". The alternative name `/diag` has the opposite prior — `msbuild /diag` and `dotnet build -v:diag` mean "run and produce the most detailed build log" — so it was rejected.

## Detailed design
[design]: #detailed-design

### The `/check` switch in script mode

The script-mode command-line parser recognizes `/check` as a bare switch in its script-only switch set, alongside `/i`, `/nostdlib`, and `/optimize`. Giving it a value (for example `/check:on`) is rejected with a switch-needs-boolean error, matching the bare-switch convention. The switch sets a boolean that flows into the shared command-line arguments object.

**Decision**: The flag is stored on the shared base argument class as `Check`, not on the language-specific arguments type. The code path that performs the check is in the shared host code, and it reads the flag from the base type. This follows the precedent of `InteractiveMode`, which the script parsers set and the shared runner reads; it avoids a language-specific cast in shared host code. The C# script parser never sets the flag, so the C# interactive host is unaffected.

### Mutual exclusion with interactive mode

`/check` requires a script file and takes priority over interactive mode. Two rules enforce this, one in the parser and one in the runner:

- **In the parser**, `/check` suppresses the automatic interactive-mode inference that would otherwise apply when no source file is given, and a `/check` invocation with no source file produces a missing-script error (`BC36963`).
- **In the runner**, when the `Check` flag is set the host goes directly to the script path and never enters the interactive loop, even when `/i` was passed.

The observable behavior is therefore:

| Invocation | Behavior |
|---|---|
| `vbi /check script.vbx` | compiles the script and reports diagnostics; exit code per the contract below |
| `vbi /check /i script.vbx` | same; `/check` wins over `/i`, and the REPL prompt never appears |
| `vbi /check` (no file) | parse-time missing-script error `BC36963`; exit code `1` |

### The check path

When the flag is set, the one-shot script path compiles the script and reports the diagnostics instead of running it:

```csharp
if (arguments.Check)
{
    var diagnostics = script.Compile(cancellationToken);
    return ReportDiagnostics(diagnostics, console.Error, errorLogger, compilation: null)
        ? Failed
        : Succeeded;
}
```

`Script.Compile()` compiles the single-submission script and returns its diagnostics without running it. The host reports those diagnostics through the shared diagnostic reporter, which prints each diagnostic to the error stream and returns whether any of them was an error.

### Exit code contract

- `0` — the script has no compilation errors; warnings may be present.
- `1` — the script has one or more compilation errors.

The exit code is exactly the return value of the shared diagnostic reporter, so the contract is "any reported error fails the gate". This matches the ordinary compiler and `dotnet build` semantics.

### Diagnostic surface

`/check` reports errors and all warnings. Info and hidden diagnostics are filtered by the underlying compile API and by the command-line reporter, so the surface is "errors plus the full warning set". This is the correct semantic for a compile gate: info and hidden diagnostics never block a build.

The success path shows warnings in full. This is the feature's main improvement over script execution, which shows nothing on a successful run. For example, a script that declares an unused local and then prints:

```vbnet
Sub S()
    Dim unusedVar As Integer
End Sub
Print("RAN")
```

is checked with `vbi /check warn.vbx`: the host reports the `BC42024` warning for the unused local and exits `0`, and the script is not executed, so `RAN` is never printed.

### Severity configuration

The first version does not add severity configuration. `/warnaserror`, `/nowarn`, and `/ruleset` are ordinary-compiler switches that the script-mode parser does not recognize, so they fall to the unrecognized-option warning (`BC2007`) and are ignored. The strictness of `/check` therefore equals the default gate of `dotnet build` and `vbc`: errors fail, warnings are shown.

**Decision**: do not extend the script parser with `/warnaserror` or `/nowarn` in this version. Escalating warnings to errors is deferred to a future analyzer-facing surface. Per-file strictness is already controllable through the source-level `Option Strict` statement, which is the Visual Basic model for per-file strictness.

### `Option Strict` and per-file strictness

The strictness of a check is determined by the file content. `Option Strict On` in a script is honored exactly as it is during execution — late binding and narrowing conversions produce their usual errors. The host supplies a default `Option Strict` value; the source-level statement overrides it per file. The check mode adds no strictness configuration of its own.

### `/check` with a `.vb` source

`/check` is a script-mode switch and has no effect on a `.vb` source. The host dispatches a `.vb` source (or an invocation carrying `/out:` or `/target:`) to the ordinary compile mode before script parsing, so `/check` is not recognized there: it produces the unrecognized-option warning (`BC2007`), and compilation proceeds and emits normally. A user who wants a compile gate over a `.vb` file uses the ordinary compiler.

### Relationship to `/removeintchecks` and `Option Checked`

`/check` is a compile gate: it compiles and reports diagnostics without running. `/removeintchecks` — and the C# compiler's `/checked` — control runtime integer-overflow checking in the emitted code. The two are unrelated, and the help text says so explicitly. A source-level `Option Checked` statement for overflow checking is a separate, future language feature and is not required for this one; should it ever be introduced, the source-level `Option` statement and the command-line switches live in two namespaces and do not interact.

### Memory emission: no disk, no execution

`Script.Compile()` builds the submission executor through the script engine's standard path, which emits the submission assembly in memory through the same in-memory assembly loader the interactive window uses. "Compile-only" therefore means no file is written and no code runs; it is not "zero compilation". A script that `#load`s preceding scripts has those preceding scripts compiled in memory as well. The wording used throughout this specification is "no disk, no execution".

### Analyzers are out of scope

`/check` reports compiler diagnostics only. Rule-style diagnostics — the fxcop and `oxlint` class — require `CompilationWithAnalyzers` and an analyzer pipeline on the script path, which is a separate proposal. `/check` is the `tsc` analogy, not the `eslint` analogy.

## Drawbacks
[drawbacks]: #drawbacks

- **No severity escalation.** Without `/warnaserror`, a gate that must fail on warnings cannot be expressed with `/check` alone. Mitigation: the full warning set is now visible, and per-file strictness is available through `Option Strict`; escalation is deferred to a future analyzer surface.
- **Memory emission is not literal "zero compile".** The submission assembly is emitted in memory through the standard script-engine path. The observable contract — no disk, no execution, no side effects — is unaffected.
- **`/check` is not honored for `.vb` sources.** A user who wants a diagnostics-only gate over a `.vb` file must use the ordinary compiler; `/check` degrades to an unrecognized-option warning there. This is intentional — the switch belongs to script mode.

## Alternatives
[alternatives]: #alternatives

- **`/diag`.** Rejected on naming. In the MSBuild toolchain, "diag" is bound to "most detailed build log" — run the build and print verbose logging — which is the opposite of "check without running". The primary naming criterion is the AI prior, and `/diag`'s prior points the wrong way.
- **`/norun` / `/noemit`.** The `tsc --noEmit` naming style, which names the suppressed action. Rejected: less readable as a verb for a mode that verifies rather than suppresses.
- **`/analysis`.** Rejected as misleading — the mode reports compiler diagnostics and includes no analyzers — and entangled with the `/analyzer:` concept.
- **Severity configuration in v1.** Extending the script parser with `/warnaserror` and `/nowarn` alongside `/check`. Rejected for the first version: strictness control belongs to the future analyzer surface, and per-file strictness is already expressible with `Option Strict`.
- **Do nothing.** Warnings remain invisible on the successful execution path, and the AI toolchain keeps no diagnostics-without-side-effects channel. The gap this proposal addresses stays open.

## Unresolved questions
[unresolved]: #unresolved-questions

None.

## Considerations
[considerations]: #considerations

### Naming and the AI prior

The primary consumer is an AI toolchain, so the naming criterion is "eliminate AI misreading": an AI interprets a bare switch from its training-data prior before reading help text. `/check` has a clean, matching prior — `cargo check`, `node --check`, and `biome check` all mean "verify, produce no artifact, do not execute". `/diag` has an opposite prior — `msbuild /diag` and `dotnet build -v:diag` mean "run and produce the most detailed build log". The naming decision therefore selects `/check`. The residual association with `/removeintchecks` is a readability concern, not a misdirection: the two semantic domains (a compile gate versus a runtime overflow behavior) are disjoint, and the help text distinguishes them in one line.

### Alignment with the ordinary compiler gate

`/check` without severity configuration is exactly as strict as a default `dotnet build` or `vbc` invocation: errors fail, warnings are shown. This keeps the gate predictable for CI and for script-authoring loops, and it is a strict improvement over the execution path, where a successful run shows no warnings at all.

### Warnings on the execution path

Script execution (`vbi script.vbx`) still compiles and runs in one step and reports diagnostics only when the run fails to compile, so a successful run continues to swallow warnings. Making execution surface warnings before running would change existing output and is deliberately left as a separate, non-blocking follow-up.

## Testing
[testing]: #testing

The feature is exercised by in-memory tests that have no side effects: they do not perform network access, process launches, or registry access. Script-file cases write only to the existing isolated-temporary-directory harness, which the host reads by necessity.

- **Parser assertions.** Without `/check` the parsed arguments leave the flag at its default; `/check` flows through the script parser into the shared argument object; `/check` with no source file produces the missing-script error; `/check /i` leaves the flag set and interactive mode on.
- **Smoke assertions.** A clean script exits `0` with no diagnostic output and is not executed; a script with an error exits `1`, reports the error, and is not executed; a script with only warnings exits `0` and shows the full warning set and is not executed; `/check /i` compiles the script and never shows the REPL prompt.
- **The `.vb` boundary.** A `.vb` source reaches the ordinary compile path; the runner-level test asserts the script parser's handling of a non-script extension. The end-to-end dispatch through the executable (compile mode, `BC2007`, normal emission) is covered by integration verification.

The full test assembly for the Visual Basic scripting host reports 206 tests with zero failures.

## Related Items
[related]: #related-items

- [cargo check](https://doc.rust-lang.org/cargo/commands/cargo-check.html) — the "check" verb: type-check, produce no artifact
- [tsc `--noEmit`](https://www.typescriptlang.org/tsconfig#noEmit) — full type-check without emit
- [node `--check`](https://nodejs.org/api/cli.html#-c---check) — syntax-check without running
- [dotnet build](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-build) — the default build gate whose strictness `/check` matches without severity configuration
- [Visual Basic `Option Strict`](https://learn.microsoft.com/en-us/dotnet/visual-basic/language-reference/statements/option-strict-statement) — the per-file strictness mechanism that determines the strictness of a check
- [vbc `/removeintchecks`](https://learn.microsoft.com/en-us/dotnet/visual-basic/reference/command-line-compiler/removeintchecks) — runtime integer-overflow checking, distinct from `/check`
