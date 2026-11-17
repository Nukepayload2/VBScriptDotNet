# Nukepayload2.Compilers.VBScriptDotNet

Reference this package from an ordinary Visual Basic project and that project is
compiled by the **VBScript.NET compiler** — the Roslyn Visual Basic fork behind
VBScript.NET — instead of the compiler that ships with the .NET SDK. The project
gains the fork's C# interoperability features with no source rewriting and no
toolchain replacement: the `Vbc` MSBuild task is redirected to the compiler in
this package, while the rest of the SDK, and the C# compilation path, stay
untouched.

## What the compiler adds

The fork consumes language constructs that stock Visual Basic cannot, all of
them defined by C# and reachable through metadata. This is **consumption only**:
Visual Basic gains no new declaration syntax, and the code you write remains
valid Visual Basic.

| Capability | C# counterpart | What it enables in a VB project |
|---|---|---|
| Byref-like types (`ref struct`) | C# 7.2+ | `Span(Of T)` and `ReadOnlySpan(Of T)` as locals, parameters, and by-value returns; `allows ref struct` generic constraints; byref-like values are kept off the managed heap, and a use that would let one escape is rejected. |
| Read-only by-reference returns (`ref readonly`) | C# 12 | Reading `ReadOnlySpan(Of T).Item` (the indexer), `GetPinnableReference()`, and `Enumerator.Current`. The value is read through automatic dereference; a write is either refused at compile time or lowered to a copy, so read-only memory is never written through. |
| Interface shared members (static abstract) | C# 11 | Generic algorithms written in VB: `T.Zero`, `T.Add(a, b)`, `T.Property = value`, `AddressOf T.Member`, `NameOf(T.Member)`, and operators such as `a + b` through a type parameter constrained to the interface, emitted as constrained calls. This is what .NET generic math (`INumber(Of T)`) is built on. |
| Extension members | C# 14 | Extension properties (`receiver.Property`, `receiver.Property = value`) and extension operators, in addition to the extension methods Visual Basic already consumed. |

A `ReadOnlySpan(Of Char)` walked with `For Each`, plus a generic `INumber(Of T)`
algorithm, both written in plain Visual Basic:

```vb
Imports System
Imports System.Numerics

Module Program
    Function Twice(Of T As INumber(Of T))(value As T) As T
        Return value + value
    End Function

    Sub Main()
        Dim text As String = "Hello, VBScript.NET!"
        Dim span As ReadOnlySpan(Of Char) = text.AsSpan()

        For Each ch In span
            Console.Write(ch)
        Next
        Console.WriteLine()

        Console.WriteLine(Twice(21))
    End Function
End Module
```

Built without this package, the same source is rejected by the SDK compiler
(`BC30668`, because `ReadOnlySpan(Of Char)` has an embedded reference, and then
`BC30643` on the span's enumerator). With the package it compiles and prints
`Hello, VBScript.NET!` followed by `42`.

## Which package do I need?

Two packages distribute this compiler. They solve different problems, and both
can be used in the same repository.

| You want to… | Use |
|---|---|
| Build a `.vbproj` or `.sln` — `dotnet build`, Visual Studio, `MSBuild.exe`, CI — with the fork compiler | **this package**, `Nukepayload2.Compilers.VBScriptDotNet` |
| Run a `.vbx` script, use the `vbi` REPL, or compile loose `.vb` files that have no project | **the CLI tool**, `Nukepayload2.Compilers.VBScriptDotNet.Cli` |
| Do both — build project code normally, and also run scripts against it | both |

This package is a build-time dependency. It contains no script host and cannot
run `.vbx` files or open a REPL; if scripts are all you need, install the CLI
tool instead. The CLI tool in turn is a script host, not a build integration —
it does not make a project's own build use the fork.

## Compiler package usage

Add a package reference to an SDK-style Visual Basic project:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Nukepayload2.Compilers.VBScriptDotNet"
                      Version="2.0.0-RC3"
                      PrivateAssets="all" />
  </ItemGroup>
</Project>
```

When the project builds, the Visual Basic part is compiled by the compiler
bundled in this package (the `Vbc` MSBuild task is redirected to the packaged
binaries). `PrivateAssets="all"` keeps that redirect local: the reference does
not flow to projects or packages that consume your library.

The package applies only to Visual Basic projects. The injected properties and
the `Vbc` task registration are conditioned on the project extension being
`.vbproj`, so referencing this package from a C# or F# project leaves that
project's compilation untouched. The package ships no C# compiler, and nothing
it does can redirect the `csc` path.

## Requirements

- For **MSBuild Core** (`dotnet build`): .NET SDK targeting **net10.0**. An
  older SDK fails with `MSB4062` — the package's task assembly targets .NET 10,
  so its `System.Runtime` cannot be loaded by that host. The failure is a load
  error, not a silent fallback to the SDK compiler.
- For **Visual Studio / .NET Framework MSBuild** (`MSBuild.exe`, Full host):
  .NET Framework 4.7.2+ and Visual Studio 2022 **17.11+** (the package loads a
  net472 task assembly and spawns a net472 `vbc.exe`).

This package targets **two MSBuild hosts**:

- `dotnet build` (Core MSBuild) uses `tasks/netcore` (net10.0 compiler).
- Visual Studio and `MSBuild.exe` (.NET Framework MSBuild) use
  `tasks/net472` (desktop compiler: netstandard2.0 Roslyn libraries loaded by a
  net472 `vbc.exe`). Both hosts redirect the `Vbc` task to this package.

Feature availability also depends on the target runtime, because the compiler
honours the runtime's declared capabilities: static abstract interface members
require .NET 7 or later, and `allows ref struct` generic parameters require
.NET 9 or later. Targeting an older framework produces a language diagnostic
rather than a silent downgrade.

## Notes

- Classic (non-SDK) .NET Framework projects are covered when they use
  `PackageReference`: Visual Studio's `Microsoft.VisualBasic.targets` chain also
  resolves the `Vbc` task through `$(RoslynTasksAssembly)`, which this package
  sets, so the packaged `tasks/net472` task and `vbc.exe` take over. The
  `packages.config` install path is not verified.
- Shared compilation is disabled (`UseSharedCompilation=false`); each build
  compiles by launching the compiler in a subprocess (no compiler server).
- This is a development dependency. It does not replace the .NET SDK toolchain,
  and it deliberately contains the Visual Basic compiler only.

## CLI tool usage

Install the tool:

```
dotnet tool install Nukepayload2.Compilers.VBScriptDotNet.Cli
```

Common commands:

```
vbi /version                  # print the product version and the Roslyn version it is based on
vbi src.vb /out:app.dll       # compile a .vb file to an assembly
vbi script.vbx -- arg1 arg2   # run a .vbx script, passing arguments to the script
vbi                           # start the interactive REPL
```

`.vbx` script files can also be executed directly on Unix-like systems with a
shebang line:

```
#!/usr/bin/env vbi
Console.WriteLine("hello from a script")
```

## License

MIT
