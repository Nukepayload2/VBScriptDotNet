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

    ' --- F-E: #load expansion pre-scan (design F-E / upstream-merge 2.16) ---
    '
    ' A submission (or file script) that #loads a file whose body carries #R "nuget:..." directives used to
    ' be pre-scanned on the submitted text only, so the loaded file's package was never restored and binding
    ' failed with BC2017 "could not find library". The coordinator now expands #load through the compilation
    ' ScriptOptions' source resolver (sharing the compiler's CollectLoadTrees walk) and scans every reachable
    ' tree. These tests use an in-memory SourceReferenceResolver so nothing touches the disk.

    ' Same real assets shape as ManagedAssetsJson, for a package named Contoso.X.
    Private Const LoadedPackageAssetsJson As String = "{""packageFolders"":{""C:/nuget/packages/"":{}},""libraries"":{""Contoso.X/1.0.0"":{""type"":""package"",""path"":""contoso.x/1.0.0""}},""targets"":{""net10.0"":{""Contoso.X/1.0.0"":{""type"":""package"",""compile"":{""lib/net10.0/Contoso.X.dll"":{}},""runtime"":{""lib/net10.0/Contoso.X.dll"":{}}}}}}"

    Private Shared Function OptionsWithMemorySource(files As IEnumerable(Of KeyValuePair(Of String, String))) As ScriptOptions
        Return ScriptOptions.Default.WithSourceResolver(New MemorySourceResolver(files))
    End Function

    <Fact>
    Public Sub LoadNestedNuGetDirectiveTriggersRestore()
        ' Regression for the user repro: #load "b.vbx" whose body has #R "nuget:Contoso.X, 1.0.0" must be
        ' pre-scanned so the package is restored before compilation (diagnostics stay empty).
        Dim runner As New FakeRestoreRunner()
        runner.Outcome = New RestoreOutcome(exitCode:=0, standardError:="", nuGetCacheWritten:=True, assetsJsonText:=LoadedPackageAssetsJson)
        Dim coordinator As New NuGetRestoreCoordinator(New NuGetPackageSession(), runner:=runner)
        Dim options = OptionsWithMemorySource({New KeyValuePair(Of String, String)("/mem/b.vbx", "#R " & Quote & "nuget:Contoso.X, 1.0.0" & Quote)})

        Dim code = "#load " & Quote & "/mem/b.vbx" & Quote
        Dim diagnostics = coordinator.PrepareCompilationAsync(SourceText.From(code), "", options, CancellationToken.None).GetAwaiter().GetResult()

        Assert.True(diagnostics.IsEmpty)
        Assert.Equal(1, runner.RestoreCalls)
    End Sub

    <Fact>
    Public Sub LoadNestedNuGetDirectiveDiagnosticAnchorsInLoadedTree()
        ' A classification diagnostic for a directive inside the loaded file must anchor in that file's tree
        ' (path + line), not in the submitted text.
        Dim coordinator As New NuGetRestoreCoordinator(New NuGetPackageSession())
        Dim options = OptionsWithMemorySource({New KeyValuePair(Of String, String)("/mem/b.vbx", "' loaded helper" & vbCrLf & "#R " & Quote & "nuget:Contoso.X" & Quote)})

        Dim code = "#load " & Quote & "/mem/b.vbx" & Quote
        Dim diagnostics = coordinator.PrepareCompilationAsync(SourceText.From(code), "", options, CancellationToken.None).GetAwaiter().GetResult()

        Assert.Equal(1, diagnostics.Length)
        Assert.Equal("VBI1001", diagnostics(0).Id)
        Assert.Equal("/mem/b.vbx", diagnostics(0).Location.GetLineSpan().Path)
        Assert.Equal(1, diagnostics(0).Location.GetLineSpan().StartLinePosition.Line)
    End Sub

    <Fact>
    Public Sub DeepNestedLoadNuGetDirectiveTriggersRestore()
        ' Depth-first expansion: main #loads b, b #loads c, c carries the nuget directive. Only the deepest
        ' loaded tree has the request, so the whole chain must be expanded for the restore to fire.
        Dim runner As New FakeRestoreRunner()
        runner.Outcome = New RestoreOutcome(exitCode:=0, standardError:="", nuGetCacheWritten:=True, assetsJsonText:=LoadedPackageAssetsJson)
        Dim coordinator As New NuGetRestoreCoordinator(New NuGetPackageSession(), runner:=runner)
        Dim options = OptionsWithMemorySource({
            New KeyValuePair(Of String, String)("/mem/b.vbx", "#load " & Quote & "/mem/c.vbx" & Quote),
            New KeyValuePair(Of String, String)("/mem/c.vbx", "#R " & Quote & "nuget:Contoso.X, 1.0.0" & Quote)})

        Dim code = "#load " & Quote & "/mem/b.vbx" & Quote
        Dim diagnostics = coordinator.PrepareCompilationAsync(SourceText.From(code), "", options, CancellationToken.None).GetAwaiter().GetResult()

        Assert.True(diagnostics.IsEmpty)
        Assert.Equal(1, runner.RestoreCalls)
    End Sub

    <Fact>
    Public Sub MainNuGetDirectiveWithoutLoadStillRestoredWithOptions()
        ' Regression: a nuget #R directly in the submitted text (no #load anywhere) keeps working when the
        ' options-carrying seam shape is used.
        Dim runner As New FakeRestoreRunner()
        runner.Outcome = New RestoreOutcome(exitCode:=0, standardError:="", nuGetCacheWritten:=True, assetsJsonText:=ManagedAssetsJson)
        Dim coordinator As New NuGetRestoreCoordinator(New NuGetPackageSession(), runner:=runner)
        Dim options = OptionsWithMemorySource(Array.Empty(Of KeyValuePair(Of String, String))())

        Dim code = "#R " & Quote & "nuget:Contoso.Lib, 2.0.0" & Quote & vbCrLf & "? 1"
        Dim diagnostics = coordinator.PrepareCompilationAsync(SourceText.From(code), "", options, CancellationToken.None).GetAwaiter().GetResult()

        Assert.True(diagnostics.IsEmpty)
        Assert.Equal(1, runner.RestoreCalls)
    End Sub

    <Fact>
    Public Sub NoNuGetNoLoadSubmissionWithOptionsTriggersNothing()
        Dim runner As New FakeRestoreRunner()
        Dim coordinator As New NuGetRestoreCoordinator(New NuGetPackageSession(), runner:=runner)
        Dim options = OptionsWithMemorySource(Array.Empty(Of KeyValuePair(Of String, String))())

        Dim diagnostics = coordinator.PrepareCompilationAsync(SourceText.From("? 1"), "", options, CancellationToken.None).GetAwaiter().GetResult()

        Assert.True(diagnostics.IsEmpty)
        Assert.Equal(0, runner.RestoreCalls)
    End Sub

    <Fact>
    Public Sub NullOptionsLegacyShapeStillScansAndRestores()
        ' options:=Nothing must behave exactly like the original seam shape: scan the submitted text only
        ' (no #load expansion) and still restore a direct nuget directive.
        Dim runner As New FakeRestoreRunner()
        runner.Outcome = New RestoreOutcome(exitCode:=0, standardError:="", nuGetCacheWritten:=True, assetsJsonText:=ManagedAssetsJson)
        Dim coordinator As New NuGetRestoreCoordinator(New NuGetPackageSession(), runner:=runner)

        Dim code = "#R " & Quote & "nuget:Contoso.Lib, 2.0.0" & Quote & vbCrLf & "? 1"
        Dim diagnostics = coordinator.PrepareCompilationAsync(SourceText.From(code), "", Nothing, CancellationToken.None).GetAwaiter().GetResult()

        Assert.True(diagnostics.IsEmpty)
        Assert.Equal(1, runner.RestoreCalls)
    End Sub

    <Fact>
    Public Sub SelfLoadCycleTerminatesWithoutRestore()
        ' A loaded file that #loads itself must stop expanding (the compiler later reports the cyclic load);
        ' the pre-scan must not hang and must not fire a restore.
        Dim runner As New FakeRestoreRunner()
        Dim coordinator As New NuGetRestoreCoordinator(New NuGetPackageSession(), runner:=runner)
        Dim options = OptionsWithMemorySource({New KeyValuePair(Of String, String)("/mem/b.vbx", "#load " & Quote & "/mem/b.vbx" & Quote)})

        Dim code = "#load " & Quote & "/mem/b.vbx" & Quote
        Dim diagnostics = coordinator.PrepareCompilationAsync(SourceText.From(code), "", options, CancellationToken.None).GetAwaiter().GetResult()

        Assert.True(diagnostics.IsEmpty)
        Assert.Equal(0, runner.RestoreCalls)
    End Sub

    <Fact>
    Public Sub MainFileSelfLoadCycleTerminatesWithoutRestore()
        ' When the submission is a file script (filePath set), the main path is seeded into the active-load
        ' set so a #load of the main file itself is a cycle and stops expanding.
        Dim runner As New FakeRestoreRunner()
        Dim coordinator As New NuGetRestoreCoordinator(New NuGetPackageSession(), runner:=runner)
        Dim options = OptionsWithMemorySource({New KeyValuePair(Of String, String)("/mem/main.vbx", "? 1")})

        Dim code = "#load " & Quote & "/mem/main.vbx" & Quote
        Dim diagnostics = coordinator.PrepareCompilationAsync(SourceText.From(code), "/mem/main.vbx", options, CancellationToken.None).GetAwaiter().GetResult()

        Assert.True(diagnostics.IsEmpty)
        Assert.Equal(0, runner.RestoreCalls)
    End Sub

    ' --- R-3: restore-failure anchor must point at the package NuGet named in stderr, not the first #R ---

    <Fact>
    Public Sub RestoreFailureAnchorsToThePackageNuGetNamedInStderr()
        ' A two-package submission where the second #R names a package NuGet cannot find. The diagnostic
        ' must name and anchor Contoso.Missing (line 2), not the first request Contoso.Good (line 1).
        Dim runner As New FakeRestoreRunner()
        runner.Outcome = New RestoreOutcome(exitCode:=1, standardError:="error NU1101: Unable to find package Contoso.Missing. No packages exist with this id in source(s): nuget.org", nuGetCacheWritten:=False, assetsJsonText:=Nothing)

        Dim code = "#R " & Quote & "nuget:Contoso.Good, 1.0.0" & Quote & vbCrLf &
                   "#R " & Quote & "nuget:Contoso.Missing, 9.9.9" & Quote & vbCrLf &
                   "? 1"
        Dim diags = RestoreScanDiagnostics(code, runner)

        Assert.Equal(1, diags.Length)
        Assert.Equal("VBI1007", diags(0).Id)
        Assert.Contains("Contoso.Missing", diags(0).GetMessage())
        Assert.Equal(1, diags(0).Location.GetLineSpan().StartLinePosition.Line)
        Assert.Equal(1, runner.RestoreCalls)
    End Sub

    ' --- C-3: same-submission duplicate #R de-duplication ---

    <Fact>
    Public Sub DuplicateSamePackageSameVersionInOneSubmissionRestoresOnce()
        ' The same package (case variant of the id, same version) written twice must feed a single restore
        ' and a single PackageReference instead of two identical references (NU1504 noise).
        Dim runner As New FakeRestoreRunner()
        runner.Outcome = New RestoreOutcome(exitCode:=0, standardError:="", nuGetCacheWritten:=True, assetsJsonText:=ManagedAssetsJson)

        Dim code = "#R " & Quote & "nuget:Contoso.Lib, 2.0.0" & Quote & vbCrLf &
                   "#R " & Quote & "NuGet:contoso.lib, 2.0.0 " & Quote & vbCrLf &
                   "? 1"
        Dim diags = RestoreScanDiagnostics(code, runner)

        Assert.True(diags.IsEmpty)
        Assert.Equal(1, runner.RestoreCalls)
        Assert.Equal(1, CountOccurrences(runner.LastRequest.ProjectXml, "PackageReference Include=" & Quote & "Contoso.Lib"))
    End Sub

    <Fact>
    Public Sub TwoVersionsOfSamePackageInOneSubmissionKeepsBothAndReportsNu1107()
        ' Two different versions of the same id in one submission are both kept (not de-duplicated), so the
        ' restore project carries two PackageReference entries and NuGet's NU1107 conflict is translated to a
        ' diagnostic anchored at a line that actually references the package.
        Dim runner As New FakeRestoreRunner()
        runner.Outcome = New RestoreOutcome(exitCode:=1, standardError:="error NU1107: Version conflict detected for Contoso.Lib. Direct dependency Contoso.Lib 1.0.0 requested Contoso.Lib 2.0.0.", nuGetCacheWritten:=False, assetsJsonText:=Nothing)

        Dim code = "#R " & Quote & "nuget:Contoso.Lib, 1.0.0" & Quote & vbCrLf &
                   "#R " & Quote & "nuget:Contoso.Lib, 2.0.0" & Quote & vbCrLf &
                   "? 1"
        Dim diags = RestoreScanDiagnostics(code, runner)

        Assert.Equal(1, diags.Length)
        Assert.Equal("VBI1009", diags(0).Id)
        Assert.Contains("NU1107", diags(0).GetMessage())
        Assert.Equal(0, diags(0).Location.GetLineSpan().StartLinePosition.Line)
        Assert.Equal(2, CountOccurrences(runner.LastRequest.ProjectXml, "PackageReference Include=" & Quote & "Contoso.Lib"))
        Assert.Equal(1, runner.RestoreCalls)
    End Sub

    ' --- R-1: session-cumulative package set (REPL cross-submission) ---

    Private Const CumulativeUnionJson As String = "{""packageFolders"":{""C:/nuget/packages/"":{}},""libraries"":{""Contoso.Lib/2.0.0"":{""type"":""package"",""path"":""contoso.lib/2.0.0""},""Contoso.B/1.0.0"":{""type"":""package"",""path"":""contoso.b/1.0.0""}},""targets"":{""net10.0"":{""Contoso.Lib/2.0.0"":{""type"":""package"",""compile"":{""lib/net10.0/Contoso.Lib.dll"":{}},""runtime"":{""lib/net10.0/Contoso.Lib.dll"":{}}},""Contoso.B/1.0.0"":{""type"":""package"",""compile"":{""lib/net10.0/Contoso.B.dll"":{}},""runtime"":{""lib/net10.0/Contoso.B.dll"":{}}}}}}"

    <Fact>
    Public Sub SessionAccumulatesPackagesAcrossSubmissionsAndRestoresUnion()
        ' REPL semantics (R-1, README "累积会话包集合"): a later submission that references a new package
        ' restores the union of every package referenced so far in one NuGet invocation, so shared transitive
        ' dependencies are resolved on a unified graph and the session keeps serving earlier packages.
        Dim runner As New FakeRestoreRunner()
        runner.Outcomes = New RestoreOutcome() {
            New RestoreOutcome(exitCode:=0, standardError:="", nuGetCacheWritten:=True, assetsJsonText:=ManagedAssetsJson),
            New RestoreOutcome(exitCode:=0, standardError:="", nuGetCacheWritten:=True, assetsJsonText:=CumulativeUnionJson)}
        Dim session As New NuGetPackageSession()
        Dim coordinator As New NuGetRestoreCoordinator(session, runner:=runner)

        Dim first = "#R " & Quote & "nuget:Contoso.Lib, 2.0.0" & Quote & vbCrLf & "? 1"
        Dim diagnostics1 = coordinator.PrepareCompilationAsync(SourceText.From(first), "", CancellationToken.None).GetAwaiter().GetResult()
        Assert.True(diagnostics1.IsEmpty)
        Assert.Equal(1, runner.RestoreCalls)

        Dim second = "#R " & Quote & "nuget:Contoso.B, 1.0.0" & Quote & vbCrLf & "? 1"
        Dim diagnostics2 = coordinator.PrepareCompilationAsync(SourceText.From(second), "", CancellationToken.None).GetAwaiter().GetResult()
        Assert.True(diagnostics2.IsEmpty)
        Assert.Equal(2, runner.RestoreCalls)

        ' The second restore ran against the accumulated union (Contoso.Lib from the first submission plus
        ' the new Contoso.B), not just the current submission's delta.
        Assert.Contains("Contoso.Lib", runner.LastRequest.ProjectXml)
        Assert.Contains("Contoso.B", runner.LastRequest.ProjectXml)

        ' The session keeps serving both packages.
        Assert.False(session.TryGetCompilePaths("Contoso.Lib", "2.0.0").IsDefaultOrEmpty)
        Assert.False(session.TryGetCompilePaths("Contoso.B", "1.0.0").IsDefaultOrEmpty)
    End Sub

    Private NotInheritable Class MemorySourceResolver
        Inherits SourceReferenceResolver

        Private ReadOnly _files As ImmutableDictionary(Of String, String)

        Friend Sub New(files As IEnumerable(Of KeyValuePair(Of String, String)))
            _files = ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, files)
        End Sub

        Public Overrides Function NormalizePath(path As String, baseFilePath As String) As String
            ' In-memory namespace keys are already normalized absolute paths.
            Return path
        End Function

        Public Overrides Function ResolveReference(path As String, baseFilePath As String) As String
            If path Is Nothing Then
                Return Nothing
            End If
            If _files.ContainsKey(path) Then
                Return path
            End If
            ' Relative reference: resolve against the directory of the loading tree, mirroring how the real
            ' file resolver combines a #load path with the referring file's location.
            If Not String.IsNullOrEmpty(baseFilePath) Then
                Dim slash = baseFilePath.LastIndexOf("/"c)
                Dim dir = If(slash >= 0, baseFilePath.Substring(0, slash + 1), "")
                Dim candidate = dir & path
                If _files.ContainsKey(candidate) Then
                    Return candidate
                End If
            End If
            Return Nothing
        End Function

        Public Overrides Function OpenRead(resolvedPath As String) As IO.Stream
            Return New IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(_files(resolvedPath)))
        End Function

        Public Overrides Function ReadText(resolvedPath As String) As SourceText
            Return SourceText.From(_files(resolvedPath))
        End Function

        Public Overrides Function Equals(obj As Object) As Boolean
            Return ReferenceEquals(Me, obj)
        End Function

        Public Overrides Function GetHashCode() As Integer
            Return _files.Count
        End Function
    End Class

    Private NotInheritable Class FakeRestoreRunner
        Implements IRestoreRunner

        Public Property SdkVersions As String() = New String() {"10.0.100"}
        Public Property Outcome As RestoreOutcome
        Public Outcomes As RestoreOutcome() = Nothing
        Public RestoreCalls As Integer
        Public LastRequest As RestoreRequest
        Private _outcomeIndex As Integer

        Public Function GetInstalledSdkVersionsAsync(cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of String)) Implements IRestoreRunner.GetInstalledSdkVersionsAsync
            Return Task.FromResult(SdkVersions.ToImmutableArray())
        End Function

        Public Function RestoreAsync(request As RestoreRequest, cancellationToken As CancellationToken) As Task(Of RestoreOutcome) Implements IRestoreRunner.RestoreAsync
            RestoreCalls += 1
            LastRequest = request
            If Outcomes IsNot Nothing AndAlso _outcomeIndex < Outcomes.Length Then
                Dim outcome = Outcomes(_outcomeIndex)
                _outcomeIndex += 1
                Return Task.FromResult(outcome)
            End If
            Return Task.FromResult(Outcome)
        End Function
    End Class

    Private Shared Function CountOccurrences(text As String, value As String) As Integer
        Dim count = 0
        Dim index = 0
        While True
            index = text.IndexOf(value, index, StringComparison.Ordinal)
            If index < 0 Then
                Exit While
            End If
            count += 1
            index += value.Length
        End While
        Return count
    End Function

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

        Public Function PrepareCompilationAsync(code As SourceText, filePath As String, options As ScriptOptions, cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of Diagnostic)) Implements INuGetRestoreCoordinator.PrepareCompilationAsync
            If code.ToString().IndexOf("nuget", StringComparison.OrdinalIgnoreCase) >= 0 Then
                RestoreAttempted = True
                Throw New InvalidOperationException("A nuget restore must not be attempted for this submission.")
            End If
            Return Task.FromResult(ImmutableArray(Of Diagnostic).Empty)
        End Function
    End Class
End Class
