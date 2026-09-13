' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System
Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting
Imports Xunit

''' <summary>
''' A binding error that is reported while a field/property initializer is bound must surface as an ordinary
''' compilation error instead of terminating the host process inside code generation
''' (<c>CodeGen\EmitExpression.vb:207</c>: "Code gen should not be invoked if there are errors.").
''' <para>
''' These cases exercise the host-visible symptom: the script/submission compile used to end in a
''' <c>Debug.Assert</c> while emitting (which is what the <c>vbi</c> probes reported as exit code 35 with no
''' diagnostics at all). Everything here is in-memory: no file, process, registry or network access.
''' </para>
''' </summary>
Public Class ScriptTopLevelCrashTests

    ''' <summary>
    ''' Need to create a <see cref="PortableExecutableReference"/> without a file path here. Scripting will
    ''' attempt to validate file paths and one does not exist for this reference as it's an in memory item.
    ''' </summary>
    Private Shared ReadOnly s_msvbReference As PortableExecutableReference = AssemblyMetadata.CreateFromImage(
        File.ReadAllBytes(GetType(Strings).Assembly.Location)).GetReference()

    Private Shared ReadOnly s_defaultOptions As ScriptOptions = ScriptOptions.Default.
        AddReferences(s_msvbReference).
        AddReferences(GetType(Task).Assembly).
        AddImports("System.Threading.Tasks")

    ''' <summary>A nested class (non-script) initializer is not an async context, so BC36937 is reported.</summary>
    Private Shared ReadOnly AwaitInitializerSource As String =
        "Class C" & vbCrLf &
        "    Dim s As Integer = Await Task.FromResult(9)" & vbCrLf &
        "End Class"

    ''' <summary>
    ''' A top-level statement is bound by the same entry, filed into the instance bucket of the submission class
    ''' and diagnosed into the same bag as the field/property initializers, so an error only it reports (BC30582:
    ''' a <c>SyncLock</c> operand of a non-reference type) has to reach the host as a diagnostic as well.
    ''' </summary>
    Private Shared ReadOnly TopLevelStatementErrorSource As String =
        "SyncLock 5" & vbCrLf &
        "    System.Console.WriteLine(""lock"")" & vbCrLf &
        "End SyncLock" & vbCrLf &
        "System.Console.WriteLine(""after"")"

    <Fact>
    Public Sub NestedClassAwaitInitializer_IsReportedInsteadOfTerminatingTheProcess()
        Dim script = VisualBasicScript.Create(AwaitInitializerSource, s_defaultOptions)

        Dim diagnostics = script.Compile()

        Assert.Contains(diagnostics, Function(d) d.Id = "BC36937" AndAlso d.Severity = DiagnosticSeverity.Error)

        ' The failed submission is reported through the regular error channel ...
        Dim ex = Assert.Throws(Of CompilationErrorException)(Sub() script.RunAsync().GetAwaiter().GetResult())
        Assert.Contains(ex.Diagnostics, Function(d) d.Id = "BC36937")
    End Sub

    <Fact>
    Public Sub TopLevelStatementError_IsReportedInsteadOfReachingCodeGeneration()
        Dim script = VisualBasicScript.Create(TopLevelStatementErrorSource, s_defaultOptions)

        Dim diagnostics = script.Compile()

        Assert.Contains(diagnostics, Function(d) d.Id = "BC30582" AndAlso d.Severity = DiagnosticSeverity.Error)
        Assert.DoesNotContain(diagnostics, Function(d) d.Id = "BC36937")

        Dim ex = Assert.Throws(Of CompilationErrorException)(Sub() script.RunAsync().GetAwaiter().GetResult())
        Assert.Contains(ex.Diagnostics, Function(d) d.Id = "BC30582")
    End Sub

    <Fact>
    Public Sub ReplNestedClassAwaitInitializer_IsReportedAndTheSessionContinues()
        ' Multi-line submission: the REPL keeps buffering until the class declaration is complete.
        Dim runner = CreateRunner(input:=
            "Class C" & vbCrLf &
            "    Dim s As Integer = Await System.Threading.Tasks.Task.FromResult(9)" & vbCrLf &
            "End Class" & vbCrLf &
            "? 1 + 2" & vbCrLf)

        runner.RunInteractive()

        Assert.Contains("BC36937", runner.Console.Error.ToString())

        ' The session survives the failed submission: the next submission runs.
        Assert.Contains(vbCrLf & "3" & vbCrLf, runner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' The statement-kind variant of the session-continuity case: the offending submission holds a top-level
    ''' statement, not a nested class.
    ''' </summary>
    <Fact>
    Public Sub ReplTopLevelStatementError_IsReportedAndTheSessionContinues()
        Dim runner = CreateRunner(input:=
            "SyncLock 5" & vbCrLf &
            "    System.Console.WriteLine(""lock"")" & vbCrLf &
            "End SyncLock" & vbCrLf &
            "? 1 + 2" & vbCrLf)

        runner.RunInteractive()

        Assert.Contains("BC30582", runner.Console.Error.ToString())

        ' The session survives the failed submission: the next submission runs.
        Assert.Contains(vbCrLf & "3" & vbCrLf, runner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' In-memory console; an existing directory is reused as <c>BuildPaths.TempDir</c> so that nothing is
    ''' written to disk.
    ''' </summary>
    Private Shared Function CreateRunner(input As String) As CommandLineRunner
        Dim buildPaths = New BuildPaths(
            clientDir:=AppContext.BaseDirectory,
            workingDir:=AppContext.BaseDirectory,
            sdkDir:=RuntimeMetadataReferenceResolver.GetDesktopFrameworkDirectory(),
            tempDir:=AppContext.BaseDirectory)
        Dim compiler = New VisualBasicInteractiveCompiler(
            Path.Combine(AppContext.BaseDirectory, "vbi.rsp"), buildPaths, {"/R:System"}, New NotImplementedAnalyzerLoader())
        Return New CommandLineRunner(New TestConsoleIO(input), compiler, VisualBasicScriptCompiler.Instance, VisualBasicObjectFormatter.Instance)
    End Function
End Class
