# Shebang Directives

* [x] Proposed
* [x] Prototype: Complete
* [x] Implementation: Complete
* [x] Specification: [Complete](spec-shebang-directive.md)

## Summary
[summary]: #summary

This specification defines a shebang directive for the Visual Basic scripting dialect: a `#!` at the very beginning of a script file, followed by the interpreter path that the operating system uses to launch the script. The compiler recognizes the `#!` line as a directive trivia and consumes the whole line as trivia, so that a script file that begins with a shebang — the standard mechanism for executable scripts on Linux and macOS — compiles cleanly, reports correct line numbers, and is understood by every consumer of the syntax tree. In a script file, the shebang line is ignored by the language: it is not an instruction, does not bind to any symbol, and has no semantic content.

The shebang directive is confined to the scripting dialect. In ordinary compilation a leading `#!` is an error. The directive is confined to the first line and, more precisely, to the first character of the file. A shebang anywhere else is an error, but is still recognized and consumed so that the tree remains well formed. Both violations are reported as errors, matching the reference C# implementation.

The C# equivalent is specified in the [ignored-directives] proposal for C# 14, where `#!` and `#:` are **ignored preprocessing directives**: the language ignores them, while the compiler and tooling can still recognize them.

## Motivation
[motivation]: #motivation

A script that is distributed as an executable file must tell the operating system which interpreter runs it. On Linux and macOS the standard mechanism is the shebang line: the kernel reads the first line of the file, takes everything after `#!` as the interpreter path, and launches the interpreter with the script file as an argument. When a Visual Basic script interpreter is installed at a known path, a script file begins with

```vbnet
#!/opt/vbi/vbi
Dim x = 1
```

For such a file to be executable, the compiler that consumes it must accept the first line. Before this feature the first line produced a parse error: the `#` began a conditional-compilation directive, the following `!` matched no directive branch, and the remainder of the line was lexed as stray tokens.

The shebang line is consumed by every program that reads the file, not only by the interpreter. An editor, a language server, or a syntax highlighter that opens an executable script must also recognize the first line; otherwise a file that runs correctly on the command line is covered in errors when it is opened. Making the shebang a directive trivia of the syntax tree gives every consumer a single, shared tree in which the first line is a harmless directive. This is the same reason the C# compiler treats a shebang as an ignored preprocessing directive rather than stripping it in the host.

## Detailed design
[design]: #detailed-design

### Recognition

A shebang directive is recognized when a conditional-compilation directive begins with a `#` and the token that follows is a `!`. The directive is represented by a `ShebangDirectiveTriviaSyntax` node — a structured trivia node derived from `DirectiveTriviaSyntax` with a `HashToken` and an `ExclamationToken` child. The syntax kind is `ShebangDirectiveTrivia`.

The token sequence needs no new lexer work: `#` followed by `!` fails to scan as a date literal and falls back to a hash token, and `!` lexes as the existing exclamation token. A `#` followed by any other token keeps its existing behavior: a malformed conditional-compilation directive.

### Position on the first line

The `#` must be the first character of the file: position 0, with no leading trivia, and not even a byte-order mark in front of it. When the byte-order mark has already been decoded by the file-reading layer, the `#` is at position 0 and the directive is valid; when the mark survives into the source text, the `#` is not the first character and the directive is an error. A leading space, a leading blank line, or a shebang on the second line are all errors.

A misplaced `#!` that is still recognized as a directive — on a line other than the first, preceded by whitespace, or written as `# !` with trivia between the two tokens — reports `ERR_ShebangDirectiveNotOnFirstLine` (an error) and is still consumed as a shebang trivia, so the tree remains complete and the rest of the file parses normally. This mirrors the reference C# implementation, which reports the position error and still parses the directive.

A byte-order mark that survives into the source text is an exception to this rule: a leading BOM character (U+FEFF) is not treated as whitespace, so the `#` is not recognized as a directive at all. No shebang node is produced and the line fails to parse with a generalized parse error (BC30037/BC30201) rather than `ERR_ShebangDirectiveNotOnFirstLine`.

### Mode gating

The shebang directive is allowed only in the scripting dialect (`SourceCodeKind.Script`). A `#!` in ordinary compilation is an error. The scripting dialect serves both script files and interactive submissions, so both accept a leading shebang; an interactive submission that begins with `#!` is accepted and ignored, matching the C# interactive behavior.

### The directive line is ignored

Everything from `#!` to the end of the line — the interpreter path and any trailing spaces — is consumed as trivia and does not participate in semantics. The path is raw text, not a string literal, so it is not subject to string-literal rules and is never resolved to a file. The line is preserved in the tree as trailing trivia of the exclamation token. Because the directive is trivia and not a statement, line numbers of subsequent diagnostics do not drift: the shebang line occupies line 1 and every later line keeps its own number.

### Content

The node exposes the interpreter path through a `Content` property that returns the path text as a string-literal token, together with a `WithContent` method that produces a new directive with a different path. The property is provided for tooling that reads the path — for example, an editor that wants to display or validate the interpreter used to run the file.

### Errors

| Code | Diagnostic | Condition |
|---|---|---|
| BC37003 | `ERR_ShebangDirectiveOnlyAllowedInScripts` | A `#!` in ordinary (non-script) compilation. Message: "'#!' directives can be only used in scripts" |
| BC37004 | `ERR_ShebangDirectiveNotOnFirstLine` | The `#` is not the first character of the file, or the `#` carries trailing trivia (as in `# !`). Message: "'#!' must be the first characters on the first line of the file" |

Both are reported as errors, matching the reference C# implementation. The two errors can occur together: a `#!` in ordinary compilation on a line other than the first reports both the mode error and the position error.

## Drawbacks
[drawbacks]: #drawbacks

- The syntax tree gains a new public node type and the syntax kind enum gains a value, enlarging the syntax API surface.
- A `#!` in the interactive window is accepted and silently ignored. A user who expects the line to do something receives no feedback. This is consistent with C# interactive, but it is behavior with no effect.
- The position rule is strict about the byte-order mark. A file with an undecoded byte-order mark before the `#` reports an error even though a shell would still run the file; C# 14 also rejects a BOM before the `#`, though in VB the mark is not treated as whitespace so the `#` is not even recognized as a directive and the failure is a generalized parse error (BC30037/BC30201).

## Alternatives
[alternatives]: #alternatives

- **Strip the line in the host.** The interpreter removes the first line before compiling. Rejected: it shifts every diagnostic line number by one — a serious cost in a language where scripts report errors — and it fixes only the interpreter. Editors and language servers that open the same file still report an error, so a file that runs on the command line is still full of errors when opened.
- **Lex the whole `#!` line as a single comment-like trivia token in the scanner.** Feasible, but it abandons the directive node shape: directive discovery cannot find a shebang, and the tree diverges from the C# shape this feature mirrors.
- **Reuse an existing directive.** `#R` requires a string literal and `#ExternalSource` is a line-mapping directive; neither matches a raw-text first-line interpreter path.
- **Do nothing.** Executable Visual Basic scripts cannot exist on Linux or macOS, because the first line of every such script is a guaranteed compile error.

## Unresolved questions
[unresolved]: #unresolved-questions

None.

## Considerations
[considerations]: #considerations

### Alignment with the C# implementation

C# 14 treats `#!` and `#:` as ignored preprocessing directives ([ignored-directives]); the shebang is recognized when `#` is followed by `!`, reported as an error when it is not the first character of the file or when it appears in a project-based program, and consumed as a preprocessing message to the end of the line. The C# language notes considered reporting the position violation as a warning — the code is harmless, only the shell would fail to recognize it — but the reference implementation reports it as an error, and this specification follows the implementation. The same holds for ordinary compilation: C# considered exempting the shebang from the project-based-program error and did not; this specification reports the mode violation as an error.

### The interactive dialect

The scripting dialect serves both script files and interactive submissions. The parser cannot tell whether a submission came from a file or from the interactive window, so the shebang is accepted in both. In the interactive window a leading `#!` is a harmless no-op, matching C# interactive. This is the same distance between the interactive dialect and ordinary code that the C# language notes accepted for its scripting dialect ([LDM-2020-04-15][ldm-2020-04-15]).

### Disabled conditional-compilation regions

A `#!` inside a region excluded by a false `#If` is not recognized as a directive at all: the text of a disabled region is not parsed for directives, so no shebang node is produced and no diagnostic is reported. The position and mode checks apply only to directives that are actually recognized — that is, a shebang in an active region. This boundary has no equivalent test on the C# side; as implemented, the VB behavior is to not recognize a shebang inside a disabled region.

### Editor rendering

Because the shebang line is language-ignored content, editors classify it as a comment. This mirrors the C# reference implementation, which classifies a shebang directive together with `//` and `/* */` comments. The whole line, including the interpreter path, is rendered as a comment.

## Testing
[testing]: #testing

The feature is exercised by in-memory tests that have no side effects: they do not perform network access, file writes, process launches, or registry access.

- **Parser tests.** A leading shebang in a script file parses as a shebang directive trivia on the first real token; the directive is discoverable through the directive API; a shebang on the second line still parses as a shebang with the error attached to the `#`; a `#!` inside a comment stays a comment; a `#` followed by a non-`!` token keeps its malformed-directive behavior; and line numbers of diagnostics after the shebang do not drift.
- **Semantic tests.** A leading shebang in a script file reports no diagnostics; a shebang on a line other than the first, a shebang with leading whitespace, and a `# !` with trivia between the two tokens report the position error; the path content — spaces, slashes, `#`, `-`, an `env -S` argument list — is accepted without error; ordinary compilation reports the mode error; ordinary compilation on a non-first line reports both errors; and both errors are errors, not warnings.
- **Boundary tests.** A byte-order mark that survives into the source text prevents recognition of the shebang; a `#!` in a disabled region produces no directive; a misplaced shebang in an active region still reports the position error; the `Content` property returns the interpreter path; `WithContent` rebuilds the directive with a new path.
- **End-to-end script tests.** A `.vbx` file whose first line is a shebang followed by ordinary code compiles with zero diagnostics and the code behaves normally; a shebang coexists with later `#R` and `#Load` directives; a trailing expression in a script that begins with a shebang keeps the existing exit-code semantics; and an interactive submission that begins with `#!` is accepted and produces no output.

## Related Items
[related]: #related-items

- [ignored-directives](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-14.0/ignored-directives.md) — the C# 14 proposal that introduces `#!` and `#:` as ignored preprocessing directives
- [LDM-2020-07-20](https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-07-20.md) — triage of the shebang scenario (issue #3507)
- [LDM-2020-09-28](https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-09-28.md) — re-triaged to a future release, contingent on `dotnet run` tooling
- [LDM-2020-04-15](https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-04-15.md) — the accepted distance between C# and its scripting dialect

[ignored-directives]: https://github.com/dotnet/csharplang/blob/main/proposals/csharp-14.0/ignored-directives.md
[ldm-2020-07-20]: https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-07-20.md
[ldm-2020-09-28]: https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-09-28.md
[ldm-2020-04-15]: https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-04-15.md
