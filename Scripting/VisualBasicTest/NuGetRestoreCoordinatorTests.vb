' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' No-side-effect L4 tests for the NuGet restore coordinator (design §D / test-plan §5).
' Covers the decision-table rows reachable without a restore result: D1 version missing, D2 prefix format
' (full-width and ASCII colon inputs), D3 empty package name, D4 suspected misspelling, D6b host-managed
' package out of scope, D9 U6-A compile-mode negative, and Z1 no-nuget zero-trigger.
' Rows D5 / D6 / D7 / D8 (SDK missing / NU restore failure / network / net48 native capability) feed on the
' restore runner result (design §E) and are deferred until that pass lands (待 V-E).

Imports System.Collections.Immutable
Imports System.IO
Imports System.Threading
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting
Imports Xunit

Public Class NuGetRestoreCoordinatorTests

    Private Const Quote As String = """"

    ' --- D1 / D2 / D3 / D4 / D6b: coordinator pre-scan classification (design §D2, no restore needed) ---

    Private Shared Function ScanDiagnostics(code As String) As ImmutableArray(Of Diagnostic)
        Dim session As New NuGetPackageSession()
        Dim coordinator As New NuGetRestoreCoordinator(session)
        Return coordinator.PrepareCompilationAsync(SourceText.From(code), "", CancellationToken.None).GetAwaiter().GetResult()
    End Function

    Private Shared Sub AssertAnchoredAtDirectiveLine(diag As Diagnostic, expectedId As String, directiveLineOneBased As Integer)
        Assert.Equal(expectedId, diag.Id)
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity)
        Assert.NotEqual(Location.None, diag.Location)
        Assert.Equal(directiveLineOneBased - 1, diag.Location.GetLineSpan().StartLinePosition.Line)
    End Sub

    <Fact>
    Public Sub D1VersionMissingReportsDiagnosticAnchoredToDirective()
        Dim diags = ScanDiagnostics("#R " & Quote & "nuget:Newtonsoft.Json" & Quote & vbCrLf & "? 1")

        Assert.Equal(1, diags.Length)
        AssertAnchoredAtDirectiveLine(diags(0), "VBI1001", directiveLineOneBased:=1)
    End Sub

    <Fact>
    Public Sub D2FullWidthColonPrefixReportsPrefixFormat()
        ' design-detailed.md:190 / test-plan.md:63 literal input uses a full-width colon (U+FF1A) after the
        ' space, i.e. "nuget ：X". It must be a prefix-format error, not a silent ordinary-path miss.
        Dim fullWidthColon = ChrW(&HFF1A)
        Dim diags = ScanDiagnostics("#R " & Quote & "nuget " & fullWidthColon & "X" & Quote & vbCrLf & "? 1")

        Assert.Equal(1, diags.Length)
        AssertAnchoredAtDirectiveLine(diags(0), "VBI1002", directiveLineOneBased:=1)
    End Sub

    <Fact>
    Public Sub D2AsciiColonWithLeadingSpaceReportsPrefixFormat()
        Dim diags = ScanDiagnostics("#R " & Quote & "nuget :X, 1.0" & Quote & vbCrLf & "? 1")

        Assert.Equal(1, diags.Length)
        AssertAnchoredAtDirectiveLine(diags(0), "VBI1002", directiveLineOneBased:=1)
    End Sub

    <Fact>
    Public Sub D3EmptyPackageNameReportsDiagnostic()
        Dim diags = ScanDiagnostics("#R " & Quote & "nuget:" & Quote & vbCrLf & "? 1")

        Assert.Equal(1, diags.Length)
        AssertAnchoredAtDirectiveLine(diags(0), "VBI1003", directiveLineOneBased:=1)
    End Sub

    <Fact>
    Public Sub D4SuspectedMisspellingReportsDiagnostic()
        Dim diags = ScanDiagnostics("#R " & Quote & "nugt:X, 1.0" & Quote & vbCrLf & "? 1")

        Assert.Equal(1, diags.Length)
        AssertAnchoredAtDirectiveLine(diags(0), "VBI1004", directiveLineOneBased:=1)
    End Sub

    <Fact>
    Public Sub D6bHostManagedPackageReportsOutOfScope()
        Dim diags = ScanDiagnostics("#R " & Quote & "nuget:Microsoft.WindowsAppSDK, 2.2.0" & Quote & vbCrLf & "? 1")

        Assert.Equal(1, diags.Length)
        AssertAnchoredAtDirectiveLine(diags(0), "VBI1005", directiveLineOneBased:=1)
    End Sub

    <Fact>
    Public Sub DiagnosticAnchorsToDirectiveLineWhenNotFirstLine()
        ' A comment line precedes the directive: the diagnostic must anchor to the #R line, not to line 1.
        Dim diags = ScanDiagnostics("' setup" & vbCrLf &
                                    "#R " & Quote & "nuget:Newtonsoft.Json" & Quote & vbCrLf &
                                    "? 1")

        Assert.Equal(1, diags.Length)
        AssertAnchoredAtDirectiveLine(diags(0), "VBI1001", directiveLineOneBased:=2)
    End Sub

    ' --- D9 / U6-A: compile mode never intercepts a /r:nuget: reference ---

    <Fact>
    Public Sub D9CompileModeDoesNotInterpretNuGetReference()
        ' A .vb source plus /r:nuget:... selects compile mode, which never constructs the interactive runner
        ' or the coordinator (the only VBI producer). Its metadata resolver carries no package resolver, so a
        ' nuget-form reference resolves to nothing and the compiler falls back to generic metadata-not-found.
        Assert.True(VbiCompileMode.IsCompileInvocation({"/r:nuget:Contoso.Widget, 1.0", "source.vb"}))

        Dim resolver = RuntimeMetadataReferenceResolver.CreateCurrentPlatformResolver(packageResolver:=Nothing)
        Dim resolved = resolver.ResolveReference("nuget:Contoso.Widget, 1.0", Nothing, MetadataReferenceProperties.Assembly)
        Assert.True(resolved.IsEmpty)
    End Sub

    ' --- Z1 / §H1: no-nuget submissions do not attempt a restore and keep normal behavior ---

    <Fact>
    Public Sub Z1NoNuGetSubmissionDoesNotAttemptRestore()
        ' The runner consults a non-null coordinator before every submission; the coordinator itself must
        ' short-circuit with no diagnostics (and no restore) when the code has no nuget directive. The stub
        ' throws only if the runner hands it code that actually asks for a nuget package.
        Dim stub As New RestoreAttemptCoordinator()
        Dim runner = CreateRunner(input:="? 1 + 2", coordinator:=stub)

        runner.RunInteractive()

        Assert.False(stub.RestoreAttempted)
        Assert.Contains("3", runner.Console.Out.ToString())
    End Sub

    ' --- D5 / D6 / D7 / D8: restore-dependent diagnostics reached through the injectable restore runner
    ' (design §E). No process is spawned and no cache file is written; the fake runner owns the outcome.

    Private Const ManagedAssetsJson As String = "{""packageFolders"":{""C:/nuget/packages/"":{}},""libraries"":{""Contoso.Lib/2.0.0"":{""type"":""package"",""path"":""contoso.lib/2.0.0""}},""targets"":{""net10.0"":{""Contoso.Lib/2.0.0"":{""type"":""package"",""compile"":{""lib/net10.0/Contoso.Lib.dll"":{}},""runtime"":{""lib/net10.0/Contoso.Lib.dll"":{}}}}}}"

    Private Const Net48NativeAssetsJson As String = "{""packageFolders"":{""C:/nuget/packages/"":{}},""libraries"":{""contoso.native/1.0.0"":{""type"":""package"",""path"":""contoso.native/1.0.0""}},""targets"":{""net48"":{""contoso.native/1.0.0"":{""type"":""package"",""compile"":{""lib/net48/Contoso.Native.dll"":{}},""runtime"":{""lib/net48/Contoso.Native.dll"":{}},""runtimeTargets"":{""runtimes/win-x64/native/e_sqlite3.dll"":{""rid"":""win-x64"",""assetType"":""native""}}}}}}"

    ' Native assets for a RID a modern .NET host never reports, so the current-RID diagnostic fires
    ' deterministically on any net10 runner (design §G2.4).
    Private Const NativeForOtherRidAssetsJson As String = "{""packageFolders"":{""C:/nuget/packages/"":{}},""libraries"":{""Contoso.Native/1.0.0"":{""type"":""package"",""path"":""contoso.native/1.0.0""}},""targets"":{""net10.0"":{""Contoso.Native/1.0.0"":{""type"":""package"",""compile"":{""lib/net10.0/Contoso.Native.dll"":{}},""runtime"":{""lib/net10.0/Contoso.Native.dll"":{}},""runtimeTargets"":{""runtimes/win7-x86/native/e_sqlite3.dll"":{""rid"":""win7-x86"",""assetType"":""native""}}}}}}"

    Private Shared Function RestoreScanDiagnostics(code As String, runner As FakeRestoreRunner, Optional hostCapability As NuGetHostCapability = Nothing) As ImmutableArray(Of Diagnostic)
        Dim session As New NuGetPackageSession(hostCapability)
        Dim coordinator As New NuGetRestoreCoordinator(session, runner:=runner)
        Return coordinator.PrepareCompilationAsync(SourceText.From(code), "", CancellationToken.None).GetAwaiter().GetResult()
    End Function

    <Fact>
    Public Sub D5SdkMissingReportsDiagnosticWithoutRestore()
        Dim runner As New FakeRestoreRunner()
        runner.SdkVersions = Array.Empty(Of String)()

        Dim diags = RestoreScanDiagnostics("#R " & Quote & "nuget:Contoso.Widget, 1.0.0" & Quote & vbCrLf & "? 1", runner)

        Assert.Equal(1, diags.Length)
        AssertAnchoredAtDirectiveLine(diags(0), "VBI1006", directiveLineOneBased:=1)
        Assert.Equal(0, runner.RestoreCalls)
    End Sub

    <Fact>
    Public Sub D6Net48NativeAssetsReportHostCapabilityDiagnostic()
        Dim runner As New FakeRestoreRunner()
        runner.Outcome = New RestoreOutcome(exitCode:=0, standardError:="", nuGetCacheWritten:=True, assetsJsonText:=Net48NativeAssetsJson)
        Dim net48 As New NuGetHostCapability("net48")

        Dim diags = RestoreScanDiagnostics("#R " & Quote & "nuget:Contoso.Native, 1.0.0" & Quote & vbCrLf & "? 1", runner, net48)

        Assert.Equal(1, diags.Length)
        AssertAnchoredAtDirectiveLine(diags(0), "VBI1010", directiveLineOneBased:=1)
        Assert.Equal(1, runner.RestoreCalls)
    End Sub

    <Fact>
    Public Sub D6cMissingNativeForCurrentRidReportsClarityDiagnostic()
        ' net10 host, restore succeeded, but the referenced package's native assets cover only another RID.
        ' The coordinator reports VBI1011 anchored at the #R line instead of letting a later P/Invoke fail.
        Dim runner As New FakeRestoreRunner()
        runner.Outcome = New RestoreOutcome(exitCode:=0, standardError:="", nuGetCacheWritten:=True, assetsJsonText:=NativeForOtherRidAssetsJson)

        Dim diags = RestoreScanDiagnostics("#R " & Quote & "nuget:Contoso.Native, 1.0.0" & Quote & vbCrLf & "? 1", runner)

        Assert.Equal(1, diags.Length)
        AssertAnchoredAtDirectiveLine(diags(0), "VBI1011", directiveLineOneBased:=1)
        Assert.Contains("Contoso.Native", diags(0).GetMessage())
        Assert.Equal(1, runner.RestoreCalls)
    End Sub

    <Fact>
    Public Sub D7Nu1101ReportsPackageNotFound()
        Dim runner As New FakeRestoreRunner()
        runner.Outcome = New RestoreOutcome(exitCode:=1, standardError:="error NU1101: Unable to find package Contoso.Widget. No packages exist with this id in source(s): nuget.org", nuGetCacheWritten:=False, assetsJsonText:=Nothing)

        Dim diags = RestoreScanDiagnostics("#R " & Quote & "nuget:Contoso.Widget, 1.0.0" & Quote & vbCrLf & "? 1", runner)

        Assert.Equal(1, diags.Length)
        AssertAnchoredAtDirectiveLine(diags(0), "VBI1007", directiveLineOneBased:=1)
        Assert.Contains("Contoso.Widget", diags(0).GetMessage())
        Assert.Equal(1, runner.RestoreCalls)
    End Sub

    <Fact>
    Public Sub D8NetworkFailureReportsUnableToConnect()
        Dim runner As New FakeRestoreRunner()
        runner.Outcome = New RestoreOutcome(exitCode:=1, standardError:="error NU1301: Unable to load the service index for source https://api.nuget.org/v3/index.json.", nuGetCacheWritten:=False, assetsJsonText:=Nothing)

        Dim diags = RestoreScanDiagnostics("#R " & Quote & "nuget:Contoso.Widget, 1.0.0" & Quote & vbCrLf & "? 1", runner)

        Assert.Equal(1, diags.Length)
        AssertAnchoredAtDirectiveLine(diags(0), "VBI1008", directiveLineOneBased:=1)
        Assert.Equal(1, runner.RestoreCalls)
    End Sub

    <Fact>
    Public Sub SamePackageSetAfterSuccessSkipsSecondRestore()
        Dim runner As New FakeRestoreRunner()
        runner.Outcome = New RestoreOutcome(exitCode:=0, standardError:="", nuGetCacheWritten:=True, assetsJsonText:=ManagedAssetsJson)
        Dim coordinator As New NuGetRestoreCoordinator(New NuGetPackageSession(), runner:=runner)
        Dim code = SourceText.From("#R " & Quote & "nuget:Contoso.Lib, 2.0.0" & Quote & vbCrLf & "? 1")

        Dim first = coordinator.PrepareCompilationAsync(code, "", CancellationToken.None).GetAwaiter().GetResult()
        Assert.True(first.IsEmpty)
        Assert.Equal(1, runner.RestoreCalls)

        Dim second = coordinator.PrepareCompilationAsync(code, "", CancellationToken.None).GetAwaiter().GetResult()
        Assert.True(second.IsEmpty)
        Assert.Equal(1, runner.RestoreCalls)
    End Sub

    Private NotInheritable Class FakeRestoreRunner
        Implements IRestoreRunner

        Public Property SdkVersions As String() = New String() {"10.0.100"}
        Public Property Outcome As RestoreOutcome
        Public RestoreCalls As Integer

        Public Function GetInstalledSdkVersionsAsync(cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of String)) Implements IRestoreRunner.GetInstalledSdkVersionsAsync
            Return Task.FromResult(SdkVersions.ToImmutableArray())
        End Function

        Public Function RestoreAsync(request As RestoreRequest, cancellationToken As CancellationToken) As Task(Of RestoreOutcome) Implements IRestoreRunner.RestoreAsync
            RestoreCalls += 1
            Return Task.FromResult(Outcome)
        End Function
    End Class

    Private Shared Function CreateRunner(Optional input As String = "", Optional coordinator As INuGetRestoreCoordinator = Nothing) As CommandLineRunner
        Dim io As New TestConsoleIO(input)

        Dim buildPaths As New BuildPaths(
            clientDir:=AppContext.BaseDirectory,
            workingDir:=AppContext.BaseDirectory,
            sdkDir:=RuntimeMetadataReferenceResolver.GetDesktopFrameworkDirectory(),
            tempDir:=Path.GetTempPath())

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

    Private NotInheritable Class RestoreAttemptCoordinator
        Implements INuGetRestoreCoordinator

        Public RestoreAttempted As Boolean

        Public Function PrepareCompilationAsync(code As SourceText, filePath As String, cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of Diagnostic)) Implements INuGetRestoreCoordinator.PrepareCompilationAsync
            If code.ToString().IndexOf("nuget", StringComparison.OrdinalIgnoreCase) >= 0 Then
                RestoreAttempted = True
                Throw New InvalidOperationException("A nuget restore must not be attempted for this submission.")
            End If
            Return Task.FromResult(ImmutableArray(Of Diagnostic).Empty)
        End Function
    End Class
End Class
