' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

' These descriptors are host-local diagnostics (not analyzer rules shipped in this assembly), so analyzer
' release tracking and message-shape analysis do not apply.
#Disable Warning RS2008, RS1032

Namespace Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting

    ''' <summary>
    ''' VB host coordinator for <c>#R "nuget:name, version"</c>. Implements the shared
    ''' <see cref="INuGetRestoreCoordinator"/> seam: before a submission or file script is compiled it
    ''' pre-scans the reference directives, classifies each against the design decision table, restores the
    ''' referenced package set when it changed (design §E), writes the session, and returns diagnostics
    ''' anchored at the offending <c>#R</c> line. A submission without NuGet directives short-circuits with
    ''' no diagnostics and never consults the SDK or restore runner.
    ''' </summary>
    Friend NotInheritable Class NuGetRestoreCoordinator
        Implements INuGetRestoreCoordinator

        Private Shared ReadOnly s_parseOptions As New VisualBasicParseOptions(
            kind:=SourceCodeKind.Script,
            languageVersion:=LanguageVersion.Latest)

        ' Host-managed packages (design §D2 denylist, §G2.6): delivered with the host, never restored.
        Private Shared ReadOnly s_hostManagedPackages As ImmutableArray(Of String) =
            ImmutableArray.Create("Microsoft.WindowsAppSDK")

        Private Shared ReadOnly s_missingVersion As New DiagnosticDescriptor(
            id:="VBI1001",
            title:="NuGet package reference is missing a version",
            messageFormat:="NuGet package reference 'nuget:{0}' must specify a version, for example 'nuget:Newtonsoft.Json, 13.0.3'.",
            category:="VBScriptingNuGet",
            defaultSeverity:=DiagnosticSeverity.Error,
            isEnabledByDefault:=True)

        Private Shared ReadOnly s_invalidPrefix As New DiagnosticDescriptor(
            id:="VBI1002",
            title:="Invalid NuGet reference prefix",
            messageFormat:="Invalid NuGet reference prefix. Expected 'nuget:' (case-insensitive) with no whitespace before the colon, directly followed by the package name.",
            category:="VBScriptingNuGet",
            defaultSeverity:=DiagnosticSeverity.Error,
            isEnabledByDefault:=True)

        Private Shared ReadOnly s_missingPackageName As New DiagnosticDescriptor(
            id:="VBI1003",
            title:="Missing package name after 'nuget:'",
            messageFormat:="Missing package name after 'nuget:'.",
            category:="VBScriptingNuGet",
            defaultSeverity:=DiagnosticSeverity.Error,
            isEnabledByDefault:=True)

        Private Shared ReadOnly s_suspectedMisspelling As New DiagnosticDescriptor(
            id:="VBI1004",
            title:="NuGet reference prefix looks misspelled",
            messageFormat:="The reference prefix looks misspelled. Did you mean 'nuget:'?",
            category:="VBScriptingNuGet",
            defaultSeverity:=DiagnosticSeverity.Error,
            isEnabledByDefault:=True)

        Private Shared ReadOnly s_hostManagedOutOfScope As New DiagnosticDescriptor(
            id:="VBI1005",
            title:="Package is managed by the host",
            messageFormat:="Package '{0}' is managed by this host and is not restored through NuGet. Use '#R ""Microsoft.WinUI""' with the WinUI host instead.",
            category:="VBScriptingNuGet",
            defaultSeverity:=DiagnosticSeverity.Error,
            isEnabledByDefault:=True)

        Private ReadOnly _session As NuGetPackageSession
        Private ReadOnly _runner As IRestoreRunner
        Private ReadOnly _cacheRootOverride As String
        Private ReadOnly _sourceFingerprint As String
        Private _lastRestoredKeys As ImmutableArray(Of String) = ImmutableArray(Of String).Empty

        Friend Sub New(session As NuGetPackageSession,
                       Optional runner As IRestoreRunner = Nothing,
                       Optional cacheRoot As String = Nothing,
                       Optional sourceFingerprint As String = "")
            If session Is Nothing Then
                Throw New ArgumentNullException(NameOf(session))
            End If
            _session = session
            _runner = If(runner, New DotNetRestoreRunner())
            _cacheRootOverride = cacheRoot
            _sourceFingerprint = If(sourceFingerprint, String.Empty)
        End Sub

        ''' <summary>
        ''' The package session this coordinator restores into (design §D/§E).
        ''' </summary>
        Friend ReadOnly Property Session As NuGetPackageSession
            Get
                Return _session
            End Get
        End Property

        Public Async Function PrepareCompilationAsync(code As SourceText, filePath As String, cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of Diagnostic)) Implements INuGetRestoreCoordinator.PrepareCompilationAsync
            If code Is Nothing Then
                Return ImmutableArray(Of Diagnostic).Empty
            End If

            Dim scan As ScanResult = ScanSubmission(code, filePath, cancellationToken)
            If Not scan.Diagnostics.IsEmpty Then
                Return scan.Diagnostics
            End If
            If scan.ValidRequests.IsEmpty Then
                Return ImmutableArray(Of Diagnostic).Empty
            End If

            Return Await EnsureRestoredAsync(scan.ValidRequests, cancellationToken)
        End Function

        ''' <summary>
        ''' Pre-scan result: blocking diagnostics from the §D2 decision-table rows that need no restore, plus
        ''' the valid package requests (parse ok, version present, not host-managed) that feed the restore.
        ''' </summary>
        Private NotInheritable Class ScanResult

            Friend Sub New(diagnostics As ImmutableArray(Of Diagnostic), validRequests As ImmutableArray(Of NuGetPackageRequest))
                _diagnostics = diagnostics
                _validRequests = validRequests
            End Sub

            Private ReadOnly _diagnostics As ImmutableArray(Of Diagnostic)
            Private ReadOnly _validRequests As ImmutableArray(Of NuGetPackageRequest)

            Friend ReadOnly Property Diagnostics As ImmutableArray(Of Diagnostic)
                Get
                    Return _diagnostics
                End Get
            End Property

            Friend ReadOnly Property ValidRequests As ImmutableArray(Of NuGetPackageRequest)
                Get
                    Return _validRequests
                End Get
            End Property
        End Class

        Private Function ScanSubmission(code As SourceText, filePath As String, cancellationToken As CancellationToken) As ScanResult
            Dim tree = VisualBasicSyntaxTree.ParseText(code, s_parseOptions, filePath, cancellationToken)
            Dim root = TryCast(tree.GetRoot(cancellationToken), CompilationUnitSyntax)
            If root Is Nothing Then
                Return New ScanResult(ImmutableArray(Of Diagnostic).Empty, ImmutableArray(Of NuGetPackageRequest).Empty)
            End If

            Dim diagnostics As New List(Of Diagnostic)()
            Dim validRequests As New List(Of NuGetPackageRequest)()

            For Each directive In root.GetReferenceDirectives()
                cancellationToken.ThrowIfCancellationRequested()

                ' Malformed string literals are left to the parser/compiler to report.
                If directive.File.ContainsDiagnostics Then
                    Continue For
                End If

                Dim value = directive.File.ValueText
                If String.IsNullOrEmpty(value) Then
                    Continue For
                End If

                Dim location = directive.GetLocation()
                Dim name As String = Nothing
                Dim version As String = Nothing

                If NuGetPackageResolver.TryParsePackageReference(value, name, version) Then
                    If String.IsNullOrEmpty(version) Then
                        diagnostics.Add(Diagnostic.Create(s_missingVersion, location, name))
                        Continue For
                    End If
                    If IsHostManagedPackage(name) Then
                        diagnostics.Add(Diagnostic.Create(s_hostManagedOutOfScope, location, name))
                        Continue For
                    End If
                    validRequests.Add(New NuGetPackageRequest(name, version, location))
                    Continue For
                End If

                ' Design §D2: everything below mirrors TryParsePackageReference's own trimming so the two stay
                ' complementary. TryParse already rejected the value because it does not start with the ASCII
                ' "nuget:" prefix.
                Dim t = value.Trim(" "c, Convert.ToChar(9), """"c)

                If t.StartsWith("nuget:", StringComparison.OrdinalIgnoreCase) Then
                    ' e.g. "nuget:" / "nuget: , 1.0" -- parse failed because the name segment is empty.
                    diagnostics.Add(Diagnostic.Create(s_missingPackageName, location))
                    Continue For
                End If

                If t.StartsWith("nuget", StringComparison.OrdinalIgnoreCase) Then
                    ' e.g. "nuget ：X" / "nuget :X" / "nuget：X" -- starts with "nuget" but the 6th character is
                    ' not an ASCII colon (a space or full-width colon U+FF1A), so the prefix is malformed.
                    diagnostics.Add(Diagnostic.Create(s_invalidPrefix, location))
                    Continue For
                End If

                Dim colonIndex = t.IndexOf(":"c)
                If colonIndex > 0 Then
                    Dim prefix = t.Substring(0, colonIndex).TrimEnd()
                    If prefix.Length <= 7 AndAlso IsNearMiss(prefix) Then
                        ' e.g. "nugt:X" / "nugett:X".
                        diagnostics.Add(Diagnostic.Create(s_suspectedMisspelling, location))
                        Continue For
                    End If
                End If

                ' No prefix attempt; an ordinary path / TPA / GAC bare name, left to the resolver.
            Next

            Return New ScanResult(diagnostics.ToImmutableArray(), validRequests.ToImmutableArray())
        End Function

        ''' <summary>
        ''' Restore pass (design §E): decides whether the referenced set changed, gates on the .NET SDK,
        ''' deploys and runs one restore through the injectable runner, maps failures to <c>#R</c>-anchored
        ''' diagnostics, and on success reads the assets and writes the session.
        ''' </summary>
        Private Async Function EnsureRestoredAsync(validRequests As ImmutableArray(Of NuGetPackageRequest), cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of Diagnostic))
            Dim keys = New List(Of String)()
            For Each pkg In validRequests
                keys.Add(pkg.CanonicalKey)
            Next
            Dim currentKeys = NuGetPackageSet.Normalize(keys)

            If Not NuGetRestorePolicy.ShouldRestore(_lastRestoredKeys, currentKeys) Then
                Return ImmutableArray(Of Diagnostic).Empty
            End If

            cancellationToken.ThrowIfCancellationRequested()

            Dim host = _session.HostCapability
            Dim net48 = host.IsNet48

            Dim installedSdks = Await _runner.GetInstalledSdkVersionsAsync(cancellationToken)
            Dim sdkVersion = NuGetSdkResolver.SelectSdkVersion(installedSdks)
            If sdkVersion Is Nothing Then
                Return CreateSingleDiagnostic(NuGetRestoreDiagnostics.CreateSdkMissingDiagnostic(validRequests(0).Location))
            End If

            Dim rid = GetRuntimeIdentifier()
            Dim cacheRoot = ResolveCacheRoot()
            Dim key = NuGetRestoreCache.ComputeKey(
                currentKeys,
                host.FrameworkNameForRestore,
                rid,
                sdkVersion,
                _sourceFingerprint,
                net48)
            Dim cacheDirectory = NuGetRestoreCache.GetCacheDirectory(cacheRoot, key)

            Dim projectXml = NuGetProjectGenerator.GenerateProjectXml(
                host.ShortTargetFramework,
                net48,
                validRequests,
                runtimeIdentifier:=rid)
            Dim globalJson = NuGetProjectGenerator.GenerateGlobalJson(sdkVersion)

            Dim restoreRequest As New RestoreRequest(cacheDirectory, projectXml, globalJson)
            Dim outcome = Await _runner.RestoreAsync(restoreRequest, cancellationToken)

            If outcome Is Nothing OrElse outcome.ExitCode <> 0 OrElse Not outcome.NuGetCacheWritten Then
                Dim affected = validRequests(0)
                Dim exitCode = If(outcome Is Nothing, -1, outcome.ExitCode)
                Dim standardError = If(outcome Is Nothing, String.Empty, outcome.StandardError)
                Return CreateSingleDiagnostic(NuGetRestoreDiagnostics.ExitCodeToDiagnostic(
                    exitCode, standardError, affected.Location, affected.Name, affected.Version))
            End If

            If outcome.AssetsJsonText Is Nothing Then
                ' NuGet cache was written but assets text is unavailable; nothing to feed the session.
                Return ImmutableArray(Of Diagnostic).Empty
            End If

            Dim assets = NuGetRestoreAssetsReader.ReadAssets(outcome.AssetsJsonText, host.FrameworkNameForRestore, rid, validRequests)
            _session.WriteRestoredAssets(assets.CompilePathsByCanonicalKey, assets.RuntimePaths, assets.NativeRootDirectories)

            If net48 AndAlso assets.HasNativeAssets Then
                ' Host-capability block (design §D2 net48 row): the restore succeeded but the net48 host
                ' cannot probe native assets. Leave _lastRestoredKeys empty so a later identical submission
                ' re-runs the cheap no-op restore and re-reports instead of slipping through.
                Return CreateSingleDiagnostic(NuGetRestoreDiagnostics.CreateNet48NativeDiagnostic(validRequests(0).Name, validRequests(0).Location))
            End If

            If Not net48 Then
                ' Clarity diagnostics for missing current-RID native assets (design §G2.4). NuGet already did
                ' the RID fallback; when a referenced package ships native assets for other RIDs only, report
                ' it up front (anchored at the package's #R line) instead of a later DllNotFoundException.
                ' Like the net48 row, _lastRestoredKeys is left empty so an identical submission re-reports.
                Dim missingNative = NuGetMissingNativeAssetsDetector.FindMissingNativeForRid(outcome.AssetsJsonText, rid, validRequests)
                If Not missingNative.IsEmpty Then
                    Dim builder = ImmutableArray.CreateBuilder(Of Diagnostic)()
                    For Each missing In missingNative
                        builder.Add(NuGetRestoreDiagnostics.CreateMissingNativeAssetsDiagnostic(
                            missing.PackageName, rid, missing.CandidateRids, FindRequestLocation(validRequests, missing.PackageName)))
                    Next
                    Return builder.ToImmutable()
                End If
            End If

            _lastRestoredKeys = currentKeys
            Return ImmutableArray(Of Diagnostic).Empty
        End Function

        Private Shared Function CreateSingleDiagnostic(diagnostic As Diagnostic) As ImmutableArray(Of Diagnostic)
            Return ImmutableArray.Create(diagnostic)
        End Function

        Private Shared Function FindRequestLocation(requests As ImmutableArray(Of NuGetPackageRequest), packageName As String) As Location
            For Each request In requests
                If String.Equals(request.Name, packageName, StringComparison.OrdinalIgnoreCase) Then
                    Return request.Location
                End If
            Next
            Return Location.None
        End Function

        Private Function ResolveCacheRoot() As String
            If Not String.IsNullOrEmpty(_cacheRootOverride) Then
                Return _cacheRootOverride
            End If
            Return NuGetRestoreCache.DefaultCacheRoot()
        End Function

        Private Shared Function GetRuntimeIdentifier() As String
#If NET10_0 Then
            Return RuntimeInformation.RuntimeIdentifier
#Else
            ' netstandard2.0 has no RuntimeIdentifier property; fall back to the OS family RID. The net48 host
            ' still only needs a RID family for NuGet's RID graph (design §E1 records the host RID in the key).
            If RuntimeInformation.IsOSPlatform(OSPlatform.Windows) Then
                Return "win"
            End If
            If RuntimeInformation.IsOSPlatform(OSPlatform.Linux) Then
                Return "linux"
            End If
            If RuntimeInformation.IsOSPlatform(OSPlatform.OSX) Then
                Return "osx"
            End If
            Return String.Empty
#End If
        End Function

        Private Shared Function IsHostManagedPackage(name As String) As Boolean
            For Each managed In s_hostManagedPackages
                If String.Equals(managed, name, StringComparison.OrdinalIgnoreCase) Then
                    Return True
                End If
            Next
            Return False
        End Function

        Private Shared Function IsNearMiss(prefix As String) As Boolean
            Dim lower = prefix.ToLowerInvariant()
            If ComputeEditDistance(lower, "nuget") <= 1 Then
                Return True
            End If

            ' One is a prefix of the other.
            If lower.Length < "nuget".Length Then
                Return "nuget".StartsWith(lower, StringComparison.Ordinal)
            End If
            Return lower.StartsWith("nuget", StringComparison.Ordinal)
        End Function

        Private Shared Function ComputeEditDistance(a As String, b As String) As Integer
            Dim m = a.Length
            Dim n = b.Length
            Dim prev(n) As Integer
            Dim curr(n) As Integer

            For j = 0 To n
                prev(j) = j
            Next

            For i = 1 To m
                curr(0) = i
                For j = 1 To n
                    Dim cost As Integer = If(a.Chars(i - 1) = b.Chars(j - 1), 0, 1)
                    Dim deletion = prev(j) + 1
                    Dim insertion = curr(j - 1) + 1
                    Dim substitution = prev(j - 1) + cost
                    Dim best = deletion
                    If insertion < best Then best = insertion
                    If substitution < best Then best = substitution
                    curr(j) = best
                Next
                For j = 0 To n
                    prev(j) = curr(j)
                Next
            Next

            Return prev(n)
        End Function
    End Class
End Namespace
