# Nukepayload2.Compilers.VBScriptDotNet

A distribution of the Visual Basic compiler based on Roslyn, built for the
VBScript.NET project. It provides two ways to use this fork's Visual Basic
compiler:

- **Compiler package** (`Nukepayload2.Compilers.VBScriptDotNet`) — reference it
  from an SDK-style Visual Basic project so that the `Vbc` MSBuild task uses
  the compiler shipped in this package.
- **CLI tool** (`Nukepayload2.Compilers.VBScriptDotNet.Cli`) — a `vbi` command
  that compiles `.vb` files, runs `.vbx` scripts, and hosts an interactive
  REPL.

This package only covers the Visual Basic compilation path. It does not replace
the .NET SDK toolchain, and the C# compilation path keeps using the compiler
that ships with the SDK.

## Requirements

- .NET SDK targeting **net10.0**. Both the compiler package and the `vbi` tool
  target .NET 10.

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
                      Version="2.0.0-Beta"
                      PrivateAssets="all" />
  </ItemGroup>
</Project>
```

When the project builds, the Visual Basic part is compiled by the compiler
bundled in this package (the `Vbc` MSBuild task is redirected to the packaged
binaries). C# projects in the same solution are unaffected and keep using the
`csc` compiler that ships with the .NET SDK.

Reference this package **only from Visual Basic projects**. It registers the
`Vbc` MSBuild task but does not ship the C# compiler, so a C# project that
references this package would not find a `csc` binary.

Notes:

- This is a v1 release that ships the .NET (Core) toolset only. Classic
  (non-SDK) .NET Framework desktop projects are not supported.
- Shared compilation is disabled (`UseSharedCompilation=false`); each build
  compiles in-process.

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
