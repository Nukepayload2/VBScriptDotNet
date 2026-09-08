# VBScript.NET (.vbx) Scripting Guide

VBScript.NET (.vbx) scripts are powerful lightweight scripts that combine the simplicity of VBScript with the full power of the .NET ecosystem. This guide covers the key concepts and patterns for writing effective .vbx scripts.

## Core Concepts

### File Structure
- .vbx files contain Visual Basic.NET code that can be executed directly
- No project files or compilation required - scripts are executed on the fly
- Full access to .NET runtime libraries and COM objects

### Target Framework Specification
Scripts run on the vbi host that launches them, and the engine mirrors that running host's target
framework (for example the .NET 10 host compiles and restores NuGet packages against `net10.0`; the
engine does not read a header comment). A header comment such as `'Attribute TargetFramework = "net48"`
is honored by the outer launcher only, which uses it to dispatch the script to a matching host process:
```vb
'Attribute TargetFramework = "net48"
```

## Key Features and Patterns

### 1. COM Interoperability
Access COM objects using CreateObject:
```vb
Dim excel = CreateObject("Excel.Application")
excel.Visible = True
excel.Workbooks.Add
excel.ActiveCell.Value = "Value from VB Interactive"
```

### 2. .NET Desktop Integration
Full access to .NET classes and libraries:
```vb
Dim newId = Guid.NewGuid.ToString
If MsgBox(newId, vbInformation Or vbYesNo, "Copy to your clipboard?") = vbYes Then
    System.Windows.Forms.Clipboard.SetText(newId)
End If
```

### 3. Reference Directives
Use #R to reference external assemblies:
```vb
#R "Microsoft.WinUI"
#R "PresentationCore.dll"
#R "PresentationFramework.dll"
#R "WindowsBase.dll"
```

NuGet packages use the same directive with a `nuget:` prefix:
```vb
#R "nuget:Microsoft.Data.Sqlite, 8.0.1"
```
The prefix is case-insensitive; the package id and version are separated by a comma (segments are
trimmed), and a version is expected so restores are reproducible (floating specs like `8.0.*` are
allowed, but the first resolved version is cached and frozen — NuGet no-op restore does not re-check
for newer releases, so change the spec or clear the vbi nuget-restore cache to pick one up). On the
.NET (net10) host vbi restores the package set with `dotnet restore` on first use, then references the
whole restored closure. This `#R "nuget:"` script form is distinct from the official file-based
`#:package id@version` syntax; the two are not interchangeable. See `Samples/SqliteNuGetDemo.vbx` for a
native-library demo.

### 4. Import Statements
Standard VB.NET imports:
```vb
Imports System.Drawing
Imports System.Windows.Forms
Imports Microsoft.UI.Xaml
Imports Microsoft.UI.Xaml.Controls
```

### 5. Async/Await Support
Modern asynchronous programming with top-level await:
```vb
Imports System.Threading.Tasks

For i = 1 To 4
    Console.WriteLine(i)
    Await Task.Delay(1000)
Next

Dim result = Await Task.FromResult(5)
Console.WriteLine(result)
Console.WriteLine("DONE!")
Console.ReadKey()
```

### 6. Command Line Arguments
Access command line arguments:
```vb
console.writeline(string.join(",",Environment.GetCommandLineArgs))
console.readkey
```

### 7. File System Operations
Work with file paths and directories:
```vb
' Set curdir to the script's directory
Environment.CurrentDirectory = Path.GetDirectoryName(Environment.GetCommandLineArgs(1))
msgbox(curdir)
```

## UI Development Patterns

### Windows Forms Applications
Create complete WinForms applications with designer-generated code:
```vb
Imports System.Drawing
Imports System.Windows.Forms

Application.Run(New Form1)

Public Class Form1
    Private Sub MonthCalendar_DateChanged() Handles MonthCalendar1.DateChanged, MonthCalendar2.DateChanged
        LblDiffResult.Text = GetDateDifferenceString(MonthCalendar2.SelectionStart, MonthCalendar1.SelectionStart)
    End Sub
End Class
```

### WPF Applications
Build WPF applications with full XAML support:
```vb
Imports System.Windows
Imports System.Windows.Controls

Dim wnd As New System.Windows.Window With {
    .Content = New DataGrid With {
        .ItemsSource = vm.CpuSetInformation,
        .AutoGenerateColumns = True
    },
    .Title = "CPU Core Information"
}
wnd.ShowDialog
```

### WinUI 3 Applications
Modern Windows UI development:
```vb
#R "Microsoft.WinUI"
Imports Microsoft.UI.Xaml
Imports Microsoft.UI.Xaml.Controls

Dim wnd As New Window
Dim stack As New StackPanel
stack.Children.Add(New TextBlock With {.Text="WinUI3 Window from vbx script"})
wnd.Content = stack
wnd.Activate
```

## Advanced Features

### Platform Invocation Services (P/Invoke)
Call Windows API functions:
```vb
<SupportedOSPlatform("Windows10.0")>
Declare Function GetSystemCpuSetInformation Lib "kernel32.dll" (
    <Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex:=1)> Information As SYSTEM_CPU_SET_INFORMATION(),
    BufferLength As UInteger,
    ByRef ReturnedLength As UInteger,
    Process As IntPtr,
    Flags As UInteger
) As <MarshalAs(UnmanagedType.Bool)> Boolean
```

### Event Handling
AddHandler and RemoveHandler work directly in top-level code:
```vb
Dim clickHandler As RoutedEventHandler = Sub() btn.Content = $"Clicked at {Now}"
AddHandler wnd.Closed, Sub() closed = True
AddHandler btn.Click, clickHandler
' Later, remove the handler:
RemoveHandler btn.Click, clickHandler
```

### Structure Definitions
Define complex data structures:
```vb
Structure SYSTEM_CPU_SET_INFORMATION
    Dim Size As Integer
    Dim Type As CPU_SET_INFORMATION_TYPE
    Dim CpuSet As CpuSet
End Structure
```

## Best Practices

### Error Handling
- Use Try-Catch blocks for robust error handling
- Check for platform compatibility when using Windows-specific APIs

### Resource Management
- Implement IDisposable pattern for objects that need cleanup
- Use Using statements for automatic resource disposal

### Performance
- Use Task.Run for CPU-bound operations to maintain UI responsiveness
- Consider async/await for I/O operations

### Security
- Validate user input and command line arguments
- Be cautious with COM object instantiation

## Execution Notes

### WinUI Specific Considerations
- WinUI doesn't have Window.ShowDialog, so manual message loops may be required:
```vb
Do Until closed
    System.Windows.Forms.Application.DoEvents
Loop
Environment.Exit(0)
```

### Application Lifecycle
- Scripts exit automatically when execution completes
- Use Environment.Exit() for immediate termination when needed
- Following the VB Function Main semantics, the process exit code comes only from an explicit
  `Return` statement: `Return <number>` sets it (e.g. `Return 42` exits with 42), a bare `Return`
  or no `Return` exits with 0. The last expression statement of the script does not set the exit code.

## Framework Compatibility

VBScript.NET scripts support multiple .NET frameworks:
- .NET Framework 4.8
- .NET 10
- Windows-specific features require Windows runtime

## Development Workflow

1. Create .vbx file with Visual Basic.NET syntax
2. Add necessary reference directives (#R)
3. Import required namespaces
4. Write script logic using full .NET capabilities
5. Test script execution
6. Deploy as standalone script file
