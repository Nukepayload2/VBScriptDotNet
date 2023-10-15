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
### Unable to run a `.vbx` file with the official `Microsoft.CodeAnalysis.Scripting.Common` package
```console
System.ArgumentException: Cannot bind to the target method because its signature is not compatible with that of the delegate type.
  + System.Reflection.RuntimeMethodInfo.CreateDelegateInternal(System.Type, Object, System.DelegateBindingFlags)
  + System.Reflection.RuntimeMethodInfo.CreateDelegate(System.Type)
  + System.Reflection.MethodInfo.CreateDelegate(Of T)()
  + Microsoft.CodeAnalysis.Scripting.ScriptBuilder.Build(Of T)(Microsoft.CodeAnalysis.Compilation, Microsoft.CodeAnalysis.DiagnosticBag, Boolean, System.Threading.CancellationToken)
  + Microsoft.CodeAnalysis.Scripting.ScriptBuilder.CreateExecutor(Of T)(Microsoft.CodeAnalysis.Scripting.ScriptCompiler, Microsoft.CodeAnalysis.Compilation, Boolean, System.Threading.CancellationToken)
```

Exception thrown by `ScriptBuilder.Build` in `roslyn\src\Scripting\Core\ScriptBuilder.cs`
```csharp
return runtimeEntryPoint.CreateDelegate<Func<object[], Task<T>>>();
```

`T` is `Int32`, but VB compiler generates a method that `T` is `Object`. `VisualBasicCompilation.CreateScriptCompilation` should use `script.ReturnType` as return type instead of hard-coded `Object`.

I've added a workaround for this issue on branch `use-modified-roslyn`

### `Imports` doesn't work in interactive mode
`Imports` doesn't work in interactive mode. It always resets to the list specified in `vbi.rsp`.

### `#Load` is completely not implemented
It doesn't even exist in the keywords list.

### Top-level `Await` doesn't compile
Use anonymous delegate as workaround. For example, the following code reads `vbi.rsp` asynchronously and prints the result in VB format.
```vbnet
(Async Function()
Return Await File.ReadAllTextAsync("vbi.rsp")
End Function).Invoke.GetAwaiter.GetResult
```

### `Yield` statement doesn't throw error in top-level code
Don't use `Yield` statement in top-level code. 

### No security warning before executing `*.vbx` scripts
Running `*.vbx` scripts can potentially harm your computer. Do not run a script if you obtained it from an untrusted source.

### Incorrect help messages
- `*.xlf` files are not converted to managed resources.
- Usages are targeting `csi` instead of `vbi`.