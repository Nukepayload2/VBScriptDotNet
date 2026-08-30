# VBScriptDotNet

VBScriptDotNet is a fork of the Roslyn Visual Basic compiler. It ships a command-line interactive host (`vbi`) and a script runner for `.vbx` files, so Visual Basic code can be run directly without a project. `.vbx` is a modern .NET replacement for `.vbs`.

Current development version: **2.0.0-Beta**. The features described below apply to this development version.

## Features

### Interactive window
- REPL with a `>` prompt, multi-line continuation, and VB-formatted value printing.
- Top-level code without wrapping: `Dim`, `Sub`, `Function`, `Class`, and `Module`.
- Top-level `Await`, `AddHandler`, and `RemoveHandler`.
- `Imports` accumulate across submissions.
- The `?` print directive is implicit: a value expression at the end of a submission prints its value in VB format without a prefix, matching the C# interactive window. The exception is `=`: without `?`, `x = 5` is an assignment; with `?`, `? x = 5` is an equality comparison, and the choice affects operator resolution.
- Directives: `#R`, `#Load`, `#help`, `/help`, `/version`, and `/?`.

### Script files (.vbx)
- Run a script with `vbi script.vbx [-- args]`.
- Select the runtime host with `' Attribute TargetFramework = "net48"` at the top of the file.
- A `#!` shebang at the top of a script is treated as a directive, so scripts can be run directly on Unix-like systems.
- Script globals: `Args` and `Print`.
- Configure script compilation with `vbi.rsp` (`/r:`, `/imports:`).

### C# interop
- Consume C# 14 extension members, including extension properties and extension operators.
- Call static abstract interface members through a type parameter constrained to the interface, such as `T.Zero` and `T.Add`.
- Use byref-like types such as `Span(Of T)`, and pass them as type arguments where the generic parameter allows ref struct.

### Script tooling
- `/check` compiles a script and reports all diagnostics without running it.
- `/optimize+` enables release optimization for script compilation. Debug remains the default.

## Examples

### #R Directive
The following code calls `Newtonsoft.Json 13.0.3` stored in the Windows NuGet package cache to serialize a number to JSON and prints the value in VB format.
```vbnet
#R "C:\Users\<your user name>\.nuget\packages\newtonsoft.json\13.0.3\lib\net10.0\Newtonsoft.Json.dll"
Newtonsoft.Json.JsonConvert.SerializeObject(1)
```

### ? Directive
Prints a value in VB format.
The following code prints `vbCrLf` or `vbLf` depending on which OS you're using.
```vbnet
? Environment.NewLine
```

The `?` prefix is optional for most expressions; a bare value expression at the end of a submission prints automatically. The exception is `=`: without `?`, `x = 5` is an assignment; with `?`, `? x = 5` is an equality comparison. The two forms select different operators, so the choice affects operator resolution.
```vbnet
Dim x = 1
? x = 5        ' False
x = 5          ' assignment, prints nothing
? x            ' 5
```

### Top-level code
You can use `Dim`, `Sub` and `Function` without wrapping them explicitly.
The following code prints the Fibonacci sequence without declaring a class or module.
```vbnet
Function Fibonacci(n As Integer) As Integer
    If n <= 1 Then
        Return n
    Else
        Return Fibonacci(n - 1) + Fibonacci(n - 2)
    End If
End Function

Sub PrintFibonacci(count As Integer)
    Console.WriteLine(String.Join(",",
        From i In Enumerable.Range(1, count)
        Select Fibonacci(i)))
End Sub

Dim count = 10
PrintFibonacci(count)
```

### Attribute comments
Specify project properties with special comments at the top of your `vbx` script files.
#### Syntax
```vbnet
' Attribute <property-name> = <value-constant-expression>
```
#### TargetFramework attribute comment
This attribute selects the script runner of `vbx` files when you run scripts by file association.
The script runner uses the .NET runtime by default.
You can choose the .NET Framework script runner with the following comment:
```vbnet
' Attribute TargetFramework = "net48"
```

Example: [Excel With Net Framework](Samples/ExcelWithNetFramework.vbx)

## Installation
<a href="ms-windows-store://pdp/?ProductId=9N210C9TDZ95&mode=mini">
   <img src="https://get.microsoft.com/images/en-us%20dark.svg" alt="Download VB Interactive" />
</a>

The Microsoft Store build is version 1.2. The current development version in this repository is 2.0.0-Beta.

## How to run

### Run with Visual Studio
- Ensure that you've installed the latest Visual Studio 2022, the .NET desktop workload and the .NET 10 SDK.
- Open `VBInteractive.sln`.
- Set [vbi](Interactive\vbi\vbi.vbproj) as the start project.
- Change the target framework to `net10.0-windows`.
- Run.

### Run with .NET SDK
- cd `Interactive\vbi`
- Run interactively with `dotnet run --framework net10.0`
- Run interactively on Windows with `dotnet run --framework net10.0-windows`
- Run in script mode with `dotnet run --framework net10.0 -- <path-to-vbx-file>`
- Run in script mode on Windows with `dotnet run --framework net10.0-windows -- <path-to-vbx-file>`

## Building from source

Install the .NET 10 SDK and build the solution from the repository root:

```
dotnet build VBInteractive.sln
```

## Known issues

- `.vbx` files cannot be run with the original version of Roslyn; they require the compiler shipped in this repository.

For more information, see https://github.com/Nukepayload2/VBScriptDotNet/issues

## License

This project is licensed under the [MIT License](License.txt).
