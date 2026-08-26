# Optimization Level for Script Compilation

* [x] Proposed
* [x] Prototype: Complete
* [x] Implementation: Complete
* [x] Specification: [Complete](spec-script-optimization-level.md)

## Summary
[summary]: #summary

This specification makes the compiler optimization level configurable for Visual Basic scripts — both the interactive window and script file execution — through the existing `/optimize` command-line switch. Before this feature, the script host compiled every submission at `OptimizationLevel.Debug`. The host hard-coded `Debug` when it constructed the script options, and the script-mode command-line parser did not recognize `/optimize` at all, so the switch was rejected as an unknown option. A CPU-bound script could therefore never be compiled with release optimizations, and the gap is compiler-level: JIT tiered compilation cannot recover the nops and unoptimized local layouts that Debug IL fixes into the assembly.

After this feature, `/optimize+` (or `/optimize`) is honored for script file execution and, at interactive startup, for the interactive window; `/optimize-` restores Debug; and the default remains Debug. The switch is accepted from the command line and from a response file, so a response file that writes `/optimize+` establishes a session-wide Release default. The optimization level is fixed when an interactive session starts and cannot be changed mid-session. `/debug` is deliberately not passed through; the script host keeps the same policy for debug information as the C# interactive host.

The C# interactive host shares the command-line runner that this feature changes, but its script-mode parser is not extended, so the behavior of C# interactive is unchanged.

## Motivation
[motivation]: #motivation

A script that runs CPU-intensive code — numeric loops, state machines, closure-heavy paths — is compiled from Debug IL. The Debug build of the Visual Basic compiler emits nops and keeps local layouts unoptimized; the JIT then optimizes the hot paths at run time, but it cannot undo the Debug decisions already fixed into the IL:

- label and local-variable optimizations run only at Release,
- synthesized local slots are reused only at Release, which is where iterators, async state machines, and closures benefit most, and
- the code generator selects a debug-friendly emission style unless the compilation is Release.

These are all gates on `OptimizationLevel.Release`, so the only way a script benefits is to compile at Release.

The switch itself was already a first-class part of the Visual Basic command line: the ordinary compiler parses `/optimize`, `/optimize+`, and `/optimize-` into the compilation options. Two separate gaps made it ineffective for scripts. First, the script-mode command-line parser, a distinct parser used by the script host, did not recognize `/optimize`, so passing the switch produced an unknown-option warning. Second, even when the switch reached the parsed arguments, the script host ignored the compilation options and built every `ScriptOptions` with a hard-coded `OptimizationLevel.Debug`. The consuming end — the script compiler — already read the optimization level from the script options, so closing the two gaps completes the pipeline.

The C# interactive host has the same structure and the same defect: it shares the command-line runner whose script-options construction is described above. The C# script-mode parser likewise does not recognize `/optimize`. This feature fixes the Visual Basic side and leaves the C# side unchanged.

## Detailed design
[design]: #detailed-design

### The `/optimize` switch in script mode

The script-mode parser of the Visual Basic command line recognizes three switch forms:

| Switch | Effect |
|---|---|
| `/optimize` | enable optimization |
| `/optimize+` | enable optimization |
| `/optimize-` | disable optimization |

Each form must be given without a value; a value form such as `/optimize:on` is rejected with an error, matching the ordinary-compiler switch. The switch sets a local boolean that defaults to `False`. The compilation options derive the optimization level from that boolean: `OptimizationLevel.Release` when it is set, `OptimizationLevel.Debug` otherwise.

### Host pass-through

The script host constructs its `ScriptOptions` from the parsed command-line arguments. The construction now reads the optimization level from the arguments' compilation options instead of hard-coding `Debug`:

```csharp
optimizationLevel: arguments.CompilationOptions.OptimizationLevel,
```

Because the switch boolean defaults to `False`, a host started without `/optimize` compiles at `OptimizationLevel.Debug` — identical to the previous behavior. Nothing else in the script-options construction changes: unsafe is allowed, overflow checking is off, the warning level is 4, and the parse options are passed through as before.

### Behavior

The following table summarizes the observable behavior. The interactive window and script file execution share the same host path, so a switch given on the command line applies to both.

| Invocation | Optimization level | `#If DEBUG` | Notes |
|---|---|---|---|
| default (no switch) | **Debug** | `False` | unchanged |
| `vbi /optimize+ script.vbx` | **Release** | `False` | CPU-bound script |
| response file writes `/optimize+` | **Release** | `False` | session-wide default |
| `vbi /optimize+` (interactive) | **Release** | `False` | fixed at startup |
| `vbi /optimize-` | **Debug** | `False` | explicit Debug |
| `vbi /define:DEBUG` | **Debug** | **`True`** | Debug configuration: default optimization with the `DEBUG` symbol |

**The response file.** A response file (`@file`) is expanded into the argument list before parsing, so `/optimize+` written in a response file takes effect exactly as if it appeared on the command line. A default response file that ships with the host contains neither `/optimize` nor `/define`, so out of the box the host compiles at Debug.

**The interactive window.** The optimization level is fixed when the session starts. Each submission is compiled as an independent compilation, and the host's option-update path deliberately preserves the optimization level, so the level is consistent for the whole session. There is no run-time switch to change it mid-session.

### Release semantics and the `DEBUG` symbol

`/optimize+` without `/define:DEBUG` means optimized IL and `#If DEBUG` evaluates to `False` — the same combination as an MSBuild Release configuration (`Optimize=true` and no `DEBUG` define). The Visual Basic compiler does not define `DEBUG` by default; predefined symbols are limited to the version and target symbols, and user symbols come only from an explicit `/define`. There is therefore no symbol to clear: with no `/define`, `#If DEBUG` is naturally `False`. The switches are orthogonal — `/optimize+` defines no symbol, and `/define:DEBUG` does not affect the optimization level — but combining them is not a meaningful configuration: a Release build with the `DEBUG` symbol set has no real use case, so this specification does not present it as one. The intended Release semantics are exactly `/optimize+` with no `/define:DEBUG`.

The default response file does not define `DEBUG`, and this specification does not add it. A "Debug configuration" — `#If DEBUG` true with Debug optimization — is assembled by the user with `/define:DEBUG`. The alternative of defining `DEBUG` in the default response file and clearing it in a Release response file was considered and rejected: the `/define` switch accumulates and can override but not remove a symbol, so a Release response file cannot cancel a `DEBUG` define without writing `DEBUG=False` or replacing the response file, a cost that outweighs the benefit.

### Orthogonality with debug information

The optimization level and the emission of debug information are independent switches. The script host continues to ignore `/debug`: `emitDebugInformation` remains `!InteractiveMode`, so the interactive window does not emit a portable debug file and script file execution always emits one. Because `/debug` is not recognized by the script-mode parser, passing it in script mode produces an unknown-option warning (BC2007) and the session continues; this matches the pre-existing behavior and is unchanged by this feature. Only `/optimize` is passed through.

### Boundary: C# interactive

The C# interactive host shares the command-line runner whose script-options construction this feature changes, but its script-mode parser does not recognize `/optimize`. Because this specification does not extend the C# parser, the shared pass-through reads a value that is always `Debug` for C# interactive, so C# interactive keeps its previous behavior.

## Drawbacks
[drawbacks]: #drawbacks

- **Debug experience is lost at Release.** Release compilation removes nops and reuses local slots, so breakpoint and variable-inspection quality in the interactive window degrades. A user who passes `/optimize+` accepts this trade-off explicitly; the default session is unaffected.
- **The session-wide default requires a response file.** "Optimize everything by default" is expressed by writing `/optimize+` in a response file rather than by an environment variable. This is a small indirection for a "change one line, optimize everything" mental model, but the response file is already the host's configuration surface.
- **The level is fixed at startup.** A session that turns CPU-intensive halfway cannot be re-optimized without restarting (see Alternatives).

## Alternatives
[alternatives]: #alternatives

- **Environment variable.** Read a `VBI_OPTIMIZE`-style variable with precedence command line > environment > default. Rejected: compile options have no environment-variable precedent in the code base, the variable would be easy to confuse with the run-time `DOTNET_*` JIT variables, and a session-wide default is already reachable through a response file.
- **Run-time switch in the session.** A `#optimize+`-style directive that changes the level mid-session. Rejected: submissions already compiled cannot be recompiled, so the switch would affect only later submissions, and a level that silently differs across a session is an unclean semantic.
- **Default to Release.** Optimize by default. Rejected: it changes the interactive debugging experience for every session and makes optimization an implicit behavior.
- **Script-header directive.** A per-file `' Attribute Optimize = "release"` directive. Rejected: a custom mechanism outside Roslyn conventions; the file-grained need is covered by the response file and the command line.
- **MSBuild-style configuration.** A `-c Release` / `--configuration` switch modeled on `dotnet run`. Rejected: the script host is not an MSBuild project and has no `Configuration` concept; the compiler-switch convention `/optimize+` already carries the established semantics.
- **Do nothing.** CPU-bound scripts remain permanently at Debug, and the gap this proposal addresses stays open.

## Unresolved questions
[unresolved]: #unresolved-questions

None.

## Considerations
[considerations]: #considerations

### Alignment with the C# interactive host

The C# interactive host compiles at Debug and does not recognize `/optimize` in script mode. This feature leaves that boundary intact: the Visual Basic side gains the switch, and the C# side is unchanged. The shared host code therefore reads `Debug` for C# interactive and the parsed level for Visual Basic interactive. Extending the C# side would be a separate change and is deliberately out of scope.

### The interactive dialect

The interactive window is a distinct dialect of Visual Basic, and the optimization level joins the set of behaviors that are configurable per host invocation rather than per submission. The distinction matters only at the boundary of the session: the level is chosen when the process starts and is then constant, so the dialect does not grow a "mixed-level" mode.

## Testing
[testing]: #testing

The feature is exercised by in-memory tests that have no side effects: they do not perform network access, process launches, or registry access. Response-file and script-file cases write only to the existing isolated-temporary-directory harness, which the host reads by necessity.

- **API tests.** `VisualBasicScript.Create` with default options produces a compilation whose options report `OptimizationLevel.Debug`; with `ScriptOptions.WithOptimizationLevel(Release)` the compilation options report `OptimizationLevel.Release`. These prove that the consuming end already honors the script options.
- **Host parse assertions.** A host built with no switch reports `Debug`; `/optimize+` reports `Release`; `/optimize-` reports `Debug`; a response file containing `/optimize+` reports `Release`; and a response file without `/optimize` reports `Debug`.
- **Smoke tests.** An interactive session started with `/optimize+` evaluates `? 1 + 2` and prints `3` with no error; a script file run with `/optimize+` executes correctly; and a default session is unchanged.
- **Debug-information boundary.** A session started with `/debug:portable` produces the unknown-option warning BC2007 in the parsed-argument errors and on the error stream, and the session continues to evaluate normally.
- **End-to-end observation.** The host-created script is local to the execution path and not reachable from the tests. End-to-end behavior is established by composition: the API consumption test plus the host parse assertions above, together with a code review of the one-line pass-through, with the smoke tests as a backstop. No internal test hook was introduced.

The feature adds 11 test cases, all green, and the full test assembly for the Visual Basic scripting host reports 194 tests with zero failures.

## Related Items
[related]: #related-items

- [vbc `/optimize`](https://learn.microsoft.com/en-us/dotnet/visual-basic/reference/command-line-compiler/optimize) — the Visual Basic compiler option whose semantics the script-mode switch follows
- [csc `/optimize`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/code-generation) — the C# compiler option and the C# interactive host's fixed behavior
