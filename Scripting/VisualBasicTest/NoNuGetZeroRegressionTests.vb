' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' No-side-effect zero-regression test for the no-nuget pipeline (design-detailed.md §H1 /
' test-plan.md §5 Z1 / README V-H1). A representative no-nuget script -- an ordinary #R
' reference directive resolved by the default runtime resolver to a single local assembly, a
' top-level declaration, a top-level assignment, and a Print statement -- runs through the
' in-memory interactive host (StringReader/StringWriter via TestConsoleIO) and its captured
' stdout must equal a golden string byte for byte.
'
' The runner consults the restore-coordinator seam before every submission when a coordinator
' is wired in (CommandLineRunner.cs RestoreNuGetReferencesAsync), so the stub here must NOT
' throw merely because it is called. It throws only if a submission actually asks for a nuget
' package, which proves that a no-nuget script never triggers an SDK / restore path while the
' seam stays active. No file is written, no process is spawned, and no network is used.

Imports System.Collections.Immutable
Imports System.IO
Imports System.Reflection
Imports System.Threading
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting
Imports My.Resources
Imports Xunit

Public Class NoNuGetZeroRegressionTests

    Private Const Quote As String = """"

    ' The runner's captured stdout and its in-memory StringWriter both use Environment.NewLine, so the
    ' expected string is assembled with Environment.NewLine to stay byte-exact on any host newline.

    ' The logo banner is part of the interactive runner's stdout and carries the build's
    ' informational version, so the expected prefix is composed from the same resources and assembly
    ' version attributes the runner prints (the same pattern CommandLineRunnerTests uses). The script
    ' portion after the prefix is a fixed literal; the whole string is compared byte for byte (ordinal),
    ' not whitespace-tolerant.
    Private Shared ReadOnly s_interactiveCompilerVersion As String =
        GetType(VisualBasicInteractiveCompiler).Assembly.
            GetCustomAttribute(Of AssemblyInformationalVersionAttribute).InformationalVersion

    Private Shared ReadOnly s_roslynVersion As String = FormatVersionWithoutRevision(
        GetType(VisualBasicCompiler).Assembly.GetName.Version)

    Private Shared ReadOnly s_logoAndHelpPrompt As String =
        String.Format(VBScriptingResources.LogoLine1, s_interactiveCompilerVersion) + Environment.NewLine +
        String.Format(VBScriptingResources.LogoLine2, s_roslynVersion) + Environment.NewLine +
        VBScriptingResources.LogoLine3 + Environment.NewLine +
        VBScriptingResources.LogoLine4 + Environment.NewLine + Environment.NewLine +
        ScriptingResources.HelpPrompt

    Private Shared Function FormatVersionWithoutRevision(version As Version) As String
        Return New Version(version.Major, version.Minor, version.Build).ToString()
    End Function

    ' The representative no-nuget script. The #R names the local System.Console assembly, which the
    ' default ScriptMetadataResolver path (TPA) resolves to exactly one reference -- the 0/1 input the
    ' shared-Core N>1 expansion leaves byte-for-byte additive. Each physical line is its own complete
    ' REPL submission; declarations and state carry across submissions just like a .vbx file body.
    Private Shared Function NoNuGetScenarioSource() As String
        Return "#R " & Quote & "System.Console" & Quote & Environment.NewLine &
               "Dim x As Integer = 41" & Environment.NewLine &
               "x += 1" & Environment.NewLine &
               "Print(x)"
    End Function

    ' Golden stdout for interactive mode. The reader echoes each input line after the "> " prompt, then
    ' the final Print(x) value line appears, and the REPL ends with a trailing prompt. Assembled with
    ' explicit Environment.NewLine so the comparison is byte-for-byte and independent of this file's own line endings.
    Private Shared Function GoldenStdout() As String
        Return s_logoAndHelpPrompt & Environment.NewLine &
               "> #R " & Quote & "System.Console" & Quote & Environment.NewLine &
               "> Dim x As Integer = 41" & Environment.NewLine &
               "> x += 1" & Environment.NewLine &
               "> Print(x)" & Environment.NewLine &
               "42" & Environment.NewLine &
               "> "
    End Function

    <Fact>
    Public Sub NoNuGetScript_PlainReferenceAndPrint_OutputMatchesGoldenByteForByte()
        Dim coordinator As New NoNuGetRestoreAttemptCoordinator()
        Dim runner = CreateRunner(input:=NoNuGetScenarioSource(), coordinator:=coordinator)

        Dim exitCode = runner.RunInteractive()
        Dim output = runner.Console.Out.ToString()

        Assert.Equal(0, exitCode)
        Assert.True(String.Equals(GoldenStdout(), output, StringComparison.Ordinal),
                    "stdout must equal the golden text byte for byte." & Environment.NewLine &
                    "--- expected ---" & Environment.NewLine & GoldenStdout() & Environment.NewLine &
                    "--- actual ---" & Environment.NewLine & output)

        ' The no-nuget full chain reports zero diagnostics: nothing on the error stream and no
        ' error color block on the combined output.
        Assert.Equal("", runner.Console.Error.ToString())
        Assert.DoesNotContain("«Red»", output)

        ' The coordinator seam stayed active (it was consulted for every submission) but never saw a
        ' nuget request, so no SDK lookup / restore was attempted.
        Assert.False(coordinator.RestoreAttempted)
    End Sub

    <Fact>
    Public Sub NoNuGetScript_CoordinatorSeamConsultedButNoRestoreAttempted()
        Dim coordinator As New NoNuGetRestoreAttemptCoordinator()
        Dim runner = CreateRunner(input:=NoNuGetScenarioSource(), coordinator:=coordinator)

        runner.RunInteractive()

        ' All four REPL submissions (the #R line plus three code lines) flow through the seam, yet the
        ' no-nuget script is never handed to a restore path.
        Assert.True(coordinator.SubmissionsSeen >= 4, "every submission must pass the coordinator seam.")
        Assert.False(coordinator.RestoreAttempted)
        Assert.Contains("42", runner.Console.Out.ToString())
    End Sub

    Private Shared Function CreateRunner(Optional input As String = "", Optional coordinator As INuGetRestoreCoordinator = Nothing) As CommandLineRunner
        Dim io As New TestConsoleIO(input)

        Dim buildPaths As New BuildPaths(
            clientDir:=AppContext.BaseDirectory,
            workingDir:=AppContext.BaseDirectory,
            sdkDir:=RuntimeMetadataReferenceResolver.GetDesktopFrameworkDirectory(),
            tempDir:=Path.GetTempPath())

        ' The vbi script argument parser does not accept /nologo (it would warn BC2007), so the logo and
        ' help banner are expected in the captured stdout and folded into the golden prefix above.
        Dim compiler As New VisualBasicInteractiveCompiler(
            Path.Combine(AppContext.BaseDirectory, "vbi.rsp"),
            buildPaths,
            {"/R:System"},
            New NotImplementedAnalyzerLoader())

        Return New CommandLineRunner(
            io,
            compiler,
            VisualBasicScriptCompiler.Instance,
            VisualBasicObjectFormatter.Instance,
            nuGetRestoreCoordinator:=coordinator)
    End Function

    ' Throws only when a submission actually requests a nuget package. Merely being consulted is not a
    ' restore attempt: the runner calls the seam for every submission when a coordinator is present.
    Private NotInheritable Class NoNuGetRestoreAttemptCoordinator
        Implements INuGetRestoreCoordinator

        Public SubmissionsSeen As Integer
        Public RestoreAttempted As Boolean

        Public Function PrepareCompilationAsync(code As SourceText, filePath As String, cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of Diagnostic)) Implements INuGetRestoreCoordinator.PrepareCompilationAsync
            SubmissionsSeen += 1
            If code.ToString().IndexOf("nuget", StringComparison.OrdinalIgnoreCase) >= 0 Then
                RestoreAttempted = True
                Throw New InvalidOperationException("A nuget restore must not be attempted for this submission.")
            End If
            Return Task.FromResult(ImmutableArray(Of Diagnostic).Empty)
        End Function
    End Class

End Class
