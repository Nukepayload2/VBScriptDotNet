' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' L4 parity harness (test-plan section 4, T-PAR-1/T-PAR-2): one .vbx is executed twice - once through vbi's
' in-process entry point (VisualBasicScript.RunInteractiveAsync, which now routes file scripts to the VB script
' engine; no child process is spawned) and once through the public IVBScriptRunner - and stdout plus the exit
' code are compared verbatim.
' The vbi side uses a stream-separating ConsoleIO instead of TestConsoleIO because TestConsoleIO deliberately
' tees stderr into stdout (the REPL expectations rely on it), which would mix diagnostics into the stdout text
' being compared. No network, no process, no repository file: every script lives under Path.GetTempPath() and is
' removed in Finally.

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting
Imports Nukepayload2.VBScriptDotNet.Hosting
Imports Xunit

Public Class HostingParityTests

    Private Shared ReadOnly s_diagnosticId As New Regex("\bBC\d+\b", RegexOptions.CultureInvariant)

    ''' <summary>T-PAR-1: Print text plus a non-zero Return is identical in both paths.</summary>
    <Fact>
    Public Async Function T_PAR_1_PrintAndReturnMatchVbi() As Task
        Dim directory = CreateTempDirectory()
        Try
            WriteScript(directory, "main.vbx",
                "Print(""hello parity"")" & Environment.NewLine &
                "Return 3" & Environment.NewLine)

            Dim vbi = RunVbi(directory, {"main.vbx"})
            Dim result = Await RunFacadeFileAsync(directory)

            ' Pinned expectations: vbi's formatter quotes strings, and a file script's exit code is its Return
            ' value. Without this the "both sides empty" comparison could pass for the wrong reason.
            Assert.Equal("""hello parity""" & Environment.NewLine, vbi.OutText)
            Assert.Equal(3, vbi.ExitCode)
            AssertParity("Print + Return 3", vbi, result)
            Assert.Null(result.Exception)
            Assert.True(result.Diagnostics.IsEmpty)
        Finally
            System.IO.Directory.Delete(directory, recursive:=True)
        End Try
    End Function

    ''' <summary>
    ''' T-PAR-1: <c>#Load</c> with a relative path resolves against the script's directory in both paths. The
    ''' loaded script is external to the repository (temp directory) and both files are removed in Finally.
    ''' </summary>
    <Fact>
    Public Async Function T_PAR_1_LoadDirectiveRelativePathMatchesVbi() As Task
        Dim directory = CreateTempDirectory()
        Try
            WriteScript(directory, "loaded.vbx",
                "Function LoadedValue() As Integer" & Environment.NewLine &
                "    Return 42" & Environment.NewLine &
                "End Function" & Environment.NewLine)
            WriteScript(directory, "main.vbx",
                "#Load ""loaded.vbx""" & Environment.NewLine &
                "Print(LoadedValue())" & Environment.NewLine &
                "Return 5" & Environment.NewLine)

            Dim vbi = RunVbi(directory, {"main.vbx"})
            Dim result = Await RunFacadeFileAsync(directory)

            Assert.Equal("42" & Environment.NewLine, vbi.OutText)
            Assert.Equal(5, vbi.ExitCode)
            AssertParity("#Load relative path", vbi, result)
            Assert.Null(result.Exception)
            Assert.True(result.Diagnostics.IsEmpty, String.Join(" | ", result.Diagnostics))
        Finally
            System.IO.Directory.Delete(directory, recursive:=True)
        End Try
    End Function

    ''' <summary>
    ''' T-PAR-2: the script argument channel (<c>Args</c>) matches vbi's, including the <c>--</c> separator form
    ''' used by Samples\FileArgs.vbx's launcher.
    ''' </summary>
    <Fact>
    Public Async Function T_PAR_2_ArgsMatchVbi() As Task
        Dim directory = CreateTempDirectory()
        Try

            ' Samples\FileArgs.vbx is the repository's argument sample; it reads Environment.GetCommandLineArgs and
            ' ends with Console.ReadKey, so the in-process harness cannot run it as-is (the argument source is
            ' process-wide and ReadKey blocks). This script exercises the script-visible Args channel the sample
            ' documents.
            WriteScript(directory, "main.vbx",
                "Print(String.Join(""|"", Args))" & Environment.NewLine &
                "Return 4" & Environment.NewLine)

            Dim vbi = RunVbi(directory, {"--", "main.vbx", "alpha", "beta"})
            Dim result = Await RunFacadeFileAsync(directory, {"alpha", "beta"})

            Assert.Equal("""alpha|beta""" & Environment.NewLine, vbi.OutText)
            Assert.Equal(4, vbi.ExitCode)
            AssertParity("Args", vbi, result)
        Finally
            System.IO.Directory.Delete(directory, recursive:=True)
        End Try
    End Function

    ''' <summary>
    ''' T-PAR-1: a compile error gives the same exit code and the same diagnostic set in both paths. The facade
    ''' exposes structured diagnostics, not the CLI's rendering, so each diagnostic is compared by its verbatim
    ''' "id: message" body inside vbi's stderr plus the distinct id sets.
    ''' </summary>
    <Fact>
    Public Async Function T_PAR_1_CompileErrorMatchesVbi() As Task
        Dim directory = CreateTempDirectory()
        Try
            WriteScript(directory, "main.vbx", "Print(notDeclared)" & Environment.NewLine)

            Dim vbi = RunVbi(directory, {"main.vbx"})
            Dim result = Await RunFacadeFileAsync(directory)

            Assert.Equal(1, vbi.ExitCode)
            Assert.Equal(1, result.ExitCode)
            AssertParity("compile error", vbi, result)
            Assert.NotEmpty(result.Diagnostics)
            AssertDiagnosticTextParity(vbi, result)
        Finally
            System.IO.Directory.Delete(directory, recursive:=True)
        End Try
    End Function

    ''' <summary>
    ''' T-PAR-1 (/check): <c>CompileFileAsync</c> against vbi's <c>/check</c> on a warning-only script. Both report
    ''' the BC42024 warning, both exit 0, and neither executes the script.
    ''' </summary>
    <Fact>
    Public Async Function T_PAR_1_CheckModeWarningMatchesVbi() As Task
        Dim directory = CreateTempDirectory()
        Try
            WriteScript(directory, "main.vbx",
                "Sub S()" & Environment.NewLine &
                "    Dim unusedVar As Integer" & Environment.NewLine &
                "End Sub" & Environment.NewLine &
                "Print(""RAN"")" & Environment.NewLine)

            Dim vbi = RunVbi(directory, {"/check", "main.vbx"})
            Dim result = Await RunFacadeCompileAsync(directory)

            Assert.Equal(0, vbi.ExitCode)
            Assert.Contains("BC42024", vbi.ErrorText)
            Assert.DoesNotContain("RAN", vbi.OutText)
            AssertParity("/check warning", vbi, result)
            Assert.Equal(0, result.ExitCode)
            Assert.Contains(result.Diagnostics,
                Function(diagnostic) diagnostic.Id = "BC42024" AndAlso
                    diagnostic.Severity = VBScriptDiagnosticSeverity.Warning)
            Assert.Equal(String.Empty, result.Output)
            Assert.Null(result.Exception)
            AssertDiagnosticTextParity(vbi, result)
        Finally
            System.IO.Directory.Delete(directory, recursive:=True)
        End Try
    End Function

    ''' <summary>
    ''' T-PAR-1: a script run-time exception keeps stdout and the exit code (the HRESULT) in parity. stderr is not
    ''' compared: the host's async stack frames differ between the two paths, so only the
    ''' exception message is checked there.
    ''' </summary>
    <Fact>
    Public Async Function T_PAR_1_RuntimeExceptionKeepsStdoutAndExitCodeParity() As Task
        Dim directory = CreateTempDirectory()
        Try
            WriteScript(directory, "main.vbx",
                "Throw New System.InvalidOperationException(""boom"")" & Environment.NewLine)

            Dim vbi = RunVbi(directory, {"main.vbx"})
            Dim result = Await RunFacadeFileAsync(directory)

            Assert.NotNull(result.Exception)
            Assert.Equal("boom", result.Exception.Message)
            AssertParity("runtime exception", vbi, result)
            Assert.Contains("boom", vbi.ErrorText)
        Finally
            System.IO.Directory.Delete(directory, recursive:=True)
        End Try
    End Function

    Private Shared Function CreateTempDirectory() As String
        Dim directory = Path.Combine(Path.GetTempPath(), "vbscript-parity-" & Guid.NewGuid().ToString("N"))
        System.IO.Directory.CreateDirectory(directory)
        Return directory
    End Function

    Private Shared Sub WriteScript(directory As String, name As String, text As String)
        File.WriteAllText(Path.Combine(directory, name), text)
    End Sub

    ''' <summary>Runs the file script through vbi's in-process entry point (no child process).</summary>
    Private Shared Function RunVbi(directory As String, args As String()) As VbiRunResult
        Dim console = ParityConsoleIO.Create()
        Dim buildPaths = New BuildPaths(
            clientDir:=AppContext.BaseDirectory,
            workingDir:=directory,
            sdkDir:=RuntimeMetadataReferenceResolver.GetDesktopFrameworkDirectory(),
            tempDir:=Path.Combine(Path.GetTempPath(), "vbscript-parity-" & Guid.NewGuid().ToString("N")))
        System.IO.Directory.CreateDirectory(buildPaths.TempDirectory)

        Try
            Dim exitCode = VisualBasicScript.RunInteractiveAsync(args, console, buildPaths, "vbi.rsp").GetAwaiter().GetResult()
            Return New VbiRunResult(exitCode, console.OutText, console.ErrorText)
        Finally
            If System.IO.Directory.Exists(buildPaths.TempDirectory) Then
                System.IO.Directory.Delete(buildPaths.TempDirectory, recursive:=True)
            End If
        End Try
    End Function

    Private Shared Async Function RunFacadeFileAsync(directory As String, Optional arguments As String() = Nothing) As Task(Of VBScriptResult)
        Using fixture As New RunnerFixture(Sub(options) options.WorkingDirectory = directory)
            Return Await fixture.Runner.RunFileAsync("main.vbx", arguments)
        End Using
    End Function

    Private Shared Async Function RunFacadeCompileAsync(directory As String) As Task(Of VBScriptResult)
        Using fixture As New RunnerFixture(Sub(options) options.WorkingDirectory = directory)
            Return Await fixture.Runner.CompileFileAsync("main.vbx")
        End Using
    End Function

    Private Shared Sub AssertParity(name As String, vbi As VbiRunResult, result As VBScriptResult)
        Console.WriteLine(
            "PARITY " & name & ": vbi.exit=" & vbi.ExitCode & "; runner.exit=" & result.ExitCode &
            "; vbi.stdout=" & Escape(vbi.OutText) & "; runner.output=" & Escape(result.Output))

        Assert.Equal(vbi.ExitCode, result.ExitCode)
        Assert.Equal(vbi.OutText, result.Output)
    End Sub

    ''' <summary>
    ''' Compares the diagnostics of both paths: the distinct BC ids must be the same set, and every facade
    ''' diagnostic's "id: message" body must appear verbatim in vbi's stderr. The CLI also prefixes the location
    ''' and the severity word, which are formatting concerns of the CLI, not of the diagnostic itself.
    ''' </summary>
    Private Shared Sub AssertDiagnosticTextParity(vbi As VbiRunResult, result As VBScriptResult)
        Assert.False(String.IsNullOrWhiteSpace(vbi.ErrorText), "vbi wrote no diagnostics to stderr")
        Console.WriteLine("PARITY vbi.stderr=" & Escape(vbi.ErrorText))

        Dim facadeIds = result.Diagnostics.
            Select(Function(diagnostic) diagnostic.Id).
            Distinct().
            OrderBy(Function(id) id, StringComparer.Ordinal).
            ToArray()

        Dim vbiIds As New List(Of String)()
        For Each match As Match In s_diagnosticId.Matches(vbi.ErrorText)
            If Not vbiIds.Contains(match.Value) Then
                vbiIds.Add(match.Value)
            End If
        Next
        vbiIds.Sort(StringComparer.Ordinal)

        Assert.Equal(Of String)(facadeIds, vbiIds.ToArray(), StringComparer.Ordinal)

        For Each diagnostic In result.Diagnostics
            Assert.Contains(diagnostic.Id & ": " & diagnostic.Message, vbi.ErrorText)
        Next
    End Sub

    Private Shared Function Escape(value As String) As String
        Return """" & value.Replace(vbCrLf, "\r\n").Replace(vbLf, "\n").Replace("""", "\""") & """"
    End Function

End Class

''' <summary>One vbi run: exit code plus the two captured console streams.</summary>
Friend NotInheritable Class VbiRunResult

    Friend ReadOnly ExitCode As Integer
    Friend ReadOnly OutText As String
    Friend ReadOnly ErrorText As String

    Friend Sub New(exitCode As Integer, outText As String, errorText As String)
        Me.ExitCode = exitCode
        Me.OutText = outText
        Me.ErrorText = errorText
    End Sub

End Class

''' <summary>
''' A <see cref="ConsoleIO"/> whose streams stay separate and whose color methods are no-ops.
''' <see cref="TestConsoleIO"/> cannot be used by the parity harness because it tees stderr into stdout (the
''' REPL expectations rely on that), which would mix diagnostics into the stdout text being compared; the
''' color no-ops keep <c>DisplayException</c> from mutating the process console.
''' </summary>
Friend NotInheritable Class ParityConsoleIO
    Inherits ConsoleIO

    Private ReadOnly _outWriter As StringWriter
    Private ReadOnly _errorWriter As StringWriter

    Friend Shared Function Create() As ParityConsoleIO
        Return New ParityConsoleIO(
            New StringWriter(Globalization.CultureInfo.InvariantCulture),
            New StringWriter(Globalization.CultureInfo.InvariantCulture))
    End Function

    Private Sub New(outWriter As StringWriter, errorWriter As StringWriter)
        MyBase.New(outWriter, errorWriter, New StringReader(String.Empty))
        _outWriter = outWriter
        _errorWriter = errorWriter
    End Sub

    Friend ReadOnly Property OutText As String
        Get
            Return _outWriter.ToString()
        End Get
    End Property

    Friend ReadOnly Property ErrorText As String
        Get
            Return _errorWriter.ToString()
        End Get
    End Property

    Public Overrides Sub SetForegroundColor(consoleColor As ConsoleColor)
    End Sub

    Public Overrides Sub ResetColor()
    End Sub

End Class
