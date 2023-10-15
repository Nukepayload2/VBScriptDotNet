# VBScriptDotNet
A patched VB interactive that runs with stable releases of Roslyn.

## How to run VB interactive
### Run with Visual Studio
- Ensure that you've installed the latest Visual Studio 2022, .NET desktop workload and .NET 6 SDK.
- Open `VBInteractive.sln`
- Set [vbi](Interactive\vbi\vbi.vbproj) as start project.
- Change target framework to `net6.0-windows`.
- Run

### Run with .NET 6 SDK
- cd `Interactive\vbi`
- Run interactively with `dotnet run --framework net6.0`
- Run interactively on Windows with `dotnet run --framework net6.0-windows`
- Run in script mode with `dotnet run --framework net6.0 -- <path-to-vbx-file>`
- Run in script mode on Windows with `dotnet run --framework net6.0-windows -- <path-to-vbx-file>`

## Available features

### Configure script compilation with `vbi.rsp`
- Use `/r:` to reference assemblies.
- Use `/imports:` to import namespaces and XML namespaces.
- For default settings, see [vbi.rsp](Interactive\vbi\vbi.coreclr.rsp).

### #R Directive
The following code calls `Newtonsoft.Json 13.0.3` stored in Windows NuGet package cache to serialize a number to JSON and prints the value in VB format.
```vbnet
#R "C:\Users\<your user name>\.nuget\packages\newtonsoft.json\13.0.3\lib\net6.0\Newtonsoft.Json.dll"
Newtonsoft.Json.JsonConvert.SerializeObject(1)
```

### ? Directive
Prints a value in VB format.
The following code prints `vbCrLf` or `vbLf` depends on which OS you're using.
```vbnet
? Environment.NewLine
```

### Top-level code
You can use `Dim`, `Sub` and `Function` without wrapping them explicitly.
The following code prints Fibonacci sequence without declaring a class or module.
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

## Known issues
See https://github.com/Nukepayload2/VBScriptDotNet/issues
