' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.IO
Imports System.Reflection
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis.Scripting
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
    ''' pre-scans the reference directives — expanding <c>#Load</c> through the compilation's
    ''' <see cref="ScriptOptions"/> when available so directives nested in loaded files are included —
    ''' classifies each against the design decision table, restores the referenced package set when it
    ''' changed (design §E), writes the session, and returns diagnostics anchored at the offending
    ''' <c>#R</c> line. A submission without NuGet directives short-circuits with no diagnostics and never
    ''' consults the SDK or restore runner.
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
        Private ReadOnly _loader As InteractiveAssemblyLoader
        Private _lastRestoredKeys As ImmutableArray(Of String) = ImmutableArray(Of String).Empty

        ' Session-cumulative package set (design §E/§F, R-1): the union of every submission's NuGet
        ' directives this host run has seen and committed after a successful restore. A submission's
        ' directives replace the session entries for the ids they mention (newest version wins across
        ' submissions) and leave every other previously referenced id in place, so each restore resolves the
        ' whole accumulated graph in one NuGet invocation. Only success commits; a failed restore leaves the
        ' prior set in force and an identical submission retries.
        Private _sessionRequests As New List(Of NuGetPackageRequest)()

        Friend Sub New(session As NuGetPackageSession,
                       Optional runner As IRestoreRunner = Nothing,
                       Optional cacheRoot As String = Nothing,
                       Optional sourceFingerprint As String = "",
                       Optional loader As InteractiveAssemblyLoader = Nothing)
            If session Is Nothing Then
                Throw New ArgumentNullException(NameOf(session))
            End If
            _session = session
            _runner = If(runner, New DotNetRestoreRunner())
            _cacheRootOverride = cacheRoot
            _sourceFingerprint = If(sourceFingerprint, String.Empty)
            _loader = loader
        End Sub

        ''' <summary>
        ''' The package session this coordinator restores into (design §D/§E).
        ''' </summary>
        Friend ReadOnly Property Session As NuGetPackageSession
            Get
                Return _session
            End Get
        End Property

        Public Async Function PrepareCompilationAsync(code As SourceText, filePath As String, options As ScriptOptions, cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of Diagnostic)) Implements INuGetRestoreCoordinator.PrepareCompilationAsync
            If code Is Nothing Then
                Return ImmutableArray(Of Diagnostic).Empty
            End If

            Dim scan As ScanResult = ScanSubmission(code, filePath, options, cancellationToken)
            If Not scan.Diagnostics.IsEmpty Then
                Return scan.Diagnostics
            End If
            If scan.ValidRequests.IsEmpty Then
                Return ImmutableArray(Of Diagnostic).Empty
            End If

            Return Await EnsureRestoredAsync(scan.ValidRequests, cancellationToken)
        End Function

        ''' <summary>
        ''' Legacy overload without the submission's <see cref="ScriptOptions"/> (no source resolver to expand
        ''' <c>#Load</c> against): scans only the submitted text, exactly like the original seam shape.
        ''' </summary>
        Public Function PrepareCompilationAsync(code As SourceText, filePath As String, cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of Diagnostic))
            Return PrepareCompilationAsync(code, filePath, Nothing, cancellationToken)
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

        ''' <summary>
        ''' Pre-scan pass: parses the submitted text and, when the compilation <see cref="ScriptOptions"/> are
        ''' available, expands <c>#Load</c> exactly like the compiler does (<see cref="VisualBasicScriptCompiler.CollectLoadTrees"/>)
        ''' so NuGet reference directives nested inside loaded files are classified too. Every reachable tree's
        ''' <c>#R</c> directives are scanned; a directive's location keeps its own tree's path, so diagnostics
        ''' and restore anchors point at the file/line that actually references the package. Without options
        ''' (or a source resolver) no <c>#Load</c> can be expanded and only the submitted text is scanned.
        ''' </summary>
        Private Function ScanSubmission(code As SourceText, filePath As String, options As ScriptOptions, cancellationToken As CancellationToken) As ScanResult
            ' ScriptOptions.ParseOptions is typed as the base ParseOptions but always holds a
            ' VisualBasicParseOptions in the VB host; parse the trees with it so loaded-file parsing matches
            ' the compiler (which uses the same options). Fall back to the coordinator default when absent.
            Dim parseOptions = s_parseOptions
            If options IsNot Nothing Then
                Dim optionsParseOptions = TryCast(options.ParseOptions, VisualBasicParseOptions)
                If optionsParseOptions IsNot Nothing Then
                    parseOptions = optionsParseOptions
                End If
            End If

            Dim tree = VisualBasicSyntaxTree.ParseText(code, parseOptions, filePath, cancellationToken)
            Dim root = TryCast(tree.GetRoot(cancellationToken), CompilationUnitSyntax)
            If root Is Nothing Then
                Return New ScanResult(ImmutableArray(Of Diagnostic).Empty, ImmutableArray(Of NuGetPackageRequest).Empty)
            End If

            Dim diagnostics As New List(Of Diagnostic)()
            Dim validRequests As New List(Of NuGetPackageRequest)()

            Dim resolver As SourceReferenceResolver = Nothing
            If options IsNot Nothing Then
                resolver = options.SourceResolver
            End If

            If resolver Is Nothing Then
                ' No source resolver: nothing to expand #Load against, so scan only the submitted tree.
                ScanTreeForNuGetDirectives(tree, diagnostics, validRequests, cancellationToken)
                Return New ScanResult(diagnostics.ToImmutableArray(), DeduplicateRequests(validRequests))
            End If

            Dim activeLoads As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            If Not String.IsNullOrEmpty(tree.FilePath) Then
                Dim normalizedMainPath = resolver.NormalizePath(tree.FilePath, Nothing)
                If normalizedMainPath IsNot Nothing Then
                    activeLoads.Add(normalizedMainPath)
                End If
            End If

            Dim loadedTrees As New List(Of SyntaxTree)()
            ' Expansion failures (missing / cyclic #Load) are left for the compiler to report at compile
            ' time; the pre-scan just stops expanding and scans what it already resolved.
            VisualBasicScriptCompiler.CollectLoadTrees(tree, parseOptions, options, activeLoads, loadedTrees)

            ScanTreeForNuGetDirectives(tree, diagnostics, validRequests, cancellationToken)
            For Each loadedTree In loadedTrees
                ScanTreeForNuGetDirectives(loadedTree, diagnostics, validRequests, cancellationToken)
            Next

            Return New ScanResult(diagnostics.ToImmutableArray(), DeduplicateRequests(validRequests))
        End Function

        ''' <summary>
        ''' De-duplicates valid requests by canonical key (id lower-cased + version, C-3): the same package at
        ''' the same version written twice — including a case variant — feeds a single restore and a single
        ''' <c>PackageReference</c>. The first directive's location is kept. Two versions of the same id in one
        ''' submission are both retained so NuGet reports the conflict (NU1107) and the R-3 anchor points at a
        ''' line that actually references the package.
        ''' </summary>
        Private Shared Function DeduplicateRequests(requests As List(Of NuGetPackageRequest)) As ImmutableArray(Of NuGetPackageRequest)
            Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim builder = ImmutableArray.CreateBuilder(Of NuGetPackageRequest)()
            For Each request In requests
                If seen.Add(request.CanonicalKey) Then
                    builder.Add(request)
                End If
            Next
            Return builder.ToImmutable()
        End Function

        ''' <summary>
        ''' Runs the design §D2 decision table over one tree's <c>#R</c> directives. Each diagnostic/request
        ''' keeps the directive's own tree location, so a directive in a <c>#Load</c>ed file anchors there.
        ''' </summary>
        Private Shared Sub ScanTreeForNuGetDirectives(
            tree As SyntaxTree,
            diagnostics As List(Of Diagnostic),
            validRequests As List(Of NuGetPackageRequest),
            cancellationToken As CancellationToken)

            Dim root = TryCast(tree.GetRoot(cancellationToken), CompilationUnitSyntax)
            If root Is Nothing Then
                Return
            End If

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
        End Sub

        ''' <summary>
        ''' Restore pass (design §E): decides whether the referenced set changed, gates on the .NET SDK,
        ''' deploys and runs one restore through the injectable runner, maps failures to <c>#R</c>-anchored
        ''' diagnostics, and on success reads the assets and writes the session.
        ''' </summary>
        Private Async Function EnsureRestoredAsync(submissionRequests As ImmutableArray(Of NuGetPackageRequest), cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of Diagnostic))
            ' Merge the submission's directives into the session-cumulative set (R-1). A submission replaces
            ' the session entries for the ids it mentions and keeps every other previously referenced id, so
            ' the restore below always sees the whole accumulated graph.
            Dim merged = MergeSessionRequests(submissionRequests)

            Dim keys = New List(Of String)()
            For Each pkg In merged
                keys.Add(pkg.CanonicalKey)
            Next
            Dim currentKeys = NuGetPackageSet.Normalize(keys)
            Dim mergedRequests = merged.ToImmutableArray()

            If Not NuGetRestorePolicy.ShouldRestore(_lastRestoredKeys, currentKeys) Then
                ' The accumulated set is unchanged (e.g. a directive was re-typed verbatim); the session
                ' already describes it, so compile proceeds with no restore. Committing the merge freshens
                ' request locations for re-typed directives.
                _sessionRequests = merged
                Return ImmutableArray(Of Diagnostic).Empty
            End If

            cancellationToken.ThrowIfCancellationRequested()

            Dim host = _session.HostCapability
            Dim net48 = host.IsNet48

            Dim installedSdks = Await _runner.GetInstalledSdkVersionsAsync(cancellationToken)
            Dim sdkVersion = NuGetSdkResolver.SelectSdkVersion(installedSdks)
            If sdkVersion Is Nothing Then
                Return CreateSingleDiagnostic(NuGetRestoreDiagnostics.CreateSdkMissingDiagnostic(submissionRequests(0).Location))
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
                mergedRequests,
                runtimeIdentifier:=rid)
            Dim globalJson = NuGetProjectGenerator.GenerateGlobalJson(sdkVersion)

            Dim restoreRequest As New RestoreRequest(cacheDirectory, projectXml, globalJson)
            Dim outcome = Await _runner.RestoreAsync(restoreRequest, cancellationToken)

            If outcome Is Nothing OrElse outcome.ExitCode <> 0 OrElse Not outcome.NuGetCacheWritten Then
                ' Failure leaves the session-cumulative set unchanged (the merge above is not committed), so
                ' an identical submission retries and a later working submission is not poisoned by the id
                ' that failed. ExitCodeToDiagnostic ties the anchor and message to the package NuGet named.
                Dim exitCode = If(outcome Is Nothing, -1, outcome.ExitCode)
                Dim standardError = If(outcome Is Nothing, String.Empty, outcome.StandardError)
                Return CreateSingleDiagnostic(NuGetRestoreDiagnostics.ExitCodeToDiagnostic(exitCode, standardError, mergedRequests))
            End If

            If outcome.AssetsJsonText Is Nothing Then
                ' NuGet cache was written but assets text is unavailable; nothing to feed the session.
                Return ImmutableArray(Of Diagnostic).Empty
            End If

            ' ReadAssets keys the assets "targets" section by the project's target-framework moniker, which is
            ' the same string written into the temporary project's <TargetFramework> (ShortTargetFramework).
            ' FrameworkNameForRestore stays a cache-key ingredient only; it does not name the assets target.
            Dim assets = NuGetRestoreAssetsReader.ReadAssets(outcome.AssetsJsonText, host.ShortTargetFramework, rid, mergedRequests)
            _session.WriteRestoredAssets(assets.CompilePathsByCanonicalKey, assets.RuntimePaths, assets.NativeRootDirectories)

            ' Host-capability blocks below leave both _sessionRequests and _lastRestoredKeys unchanged: the
            ' restore succeeded but the submission is blocked, so its packages must not poison later
            ' submissions (they were never compiled/run), while an identical submission re-runs the cheap
            ' no-op restore and re-reports instead of slipping through.

            If net48 AndAlso assets.HasNativeAssets Then
                ' Host-capability block (design §D2 net48 row): the restore succeeded but the net48 host
                ' cannot probe native assets.
                Return CreateSingleDiagnostic(NuGetRestoreDiagnostics.CreateNet48NativeDiagnostic(submissionRequests(0).Name, submissionRequests(0).Location))
            End If

            If Not net48 Then
                ' Clarity diagnostics for missing current-RID native assets (design §G2.4). NuGet already did
                ' the RID fallback; when a referenced package ships native assets for other RIDs only, report
                ' it up front (anchored at the package's #R line) instead of a later DllNotFoundException.
                Dim missingNative = NuGetMissingNativeAssetsDetector.FindMissingNativeForRid(outcome.AssetsJsonText, rid, mergedRequests)
                If Not missingNative.IsEmpty Then
                    Dim builder = ImmutableArray.CreateBuilder(Of Diagnostic)()
                    For Each missing In missingNative
                        builder.Add(NuGetRestoreDiagnostics.CreateMissingNativeAssetsDiagnostic(
                            missing.PackageName, rid, missing.CandidateRids, FindRequestLocation(mergedRequests, missing.PackageName)))
                    Next
                    Return builder.ToImmutable()
                End If
            End If

            ' Fully successful path: commit the merged set so later submissions merge on top of it, then run
            ' the runtime handshake (design §G1/§G2) — with the loader wired in, push the restored runtime
            ' (lib) assets and native probe roots into it before the script is compiled and run. Empty root
            ' sets / an absent loader leave the loader untouched.
            _sessionRequests = merged
            PushSessionAssetsToLoader(assets)

            _lastRestoredKeys = currentKeys
            Return ImmutableArray(Of Diagnostic).Empty
        End Function

        ''' <summary>
        ''' Merges <paramref name="submission"/> into the session-cumulative set (R-1): every id the
        ''' submission mentions replaces the session's entry for that id (so a newer version written in a
        ''' later submission wins and the old one stops being restored), and ids the submission does not
        ''' mention keep their session entry, making the set cumulative. Two versions of the same id inside
        ''' one submission are both kept for NuGet to report (NU1107). Never mutates <see cref="_sessionRequests"/>;
        ''' callers commit the returned list only once the restore they drive succeeds.
        ''' </summary>
        Private Function MergeSessionRequests(submission As ImmutableArray(Of NuGetPackageRequest)) As List(Of NuGetPackageRequest)
            Dim merged As New List(Of NuGetPackageRequest)()

            Dim submissionIds As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each request In submission
                submissionIds.Add(request.Name)
            Next

            For Each existing In _sessionRequests
                If Not submissionIds.Contains(existing.Name) Then
                    merged.Add(existing)
                End If
            Next

            For Each request In submission
                merged.Add(request)
            Next
            Return merged
        End Function

        ''' <summary>
        ''' Hands the restored assets to the script loader (design §G1/§G2): native probe roots become
        ''' loader probe roots and compile (ref) assets that have a distinct runtime (implementation) file are
        ''' registered as runtime-path overrides, so the loader never loads a ref-only assembly at runtime.
        ''' No-op when the host did not wire a loader (pure no-nuget / tests without a loader).
        ''' </summary>
        Private Sub PushSessionAssetsToLoader(assets As NuGetRestoreAssets)
            If _loader Is Nothing Then
                Return
            End If

            ' R-2: the loader reflects exactly this restore's session state. Reset the handshake state first
            ' so roots/overrides from an earlier restore (an upgraded/downgraded package version, or a graph
            ' that no longer carries native assets) are replaced rather than accumulated. Managed dependency
            ' registrations are intentionally not reset — earlier submissions' assemblies stay resolvable.
            _loader.ResetSessionState()

            For Each nativeRoot In assets.NativeRootDirectories
                _loader.AddNativeProbeRoot(nativeRoot)
            Next

            Dim runtimeOverrides = NuGetRestoreAssetsReader.ComputeRuntimePathOverrides(
                assets.CompilePathsByCanonicalKey, assets.RuntimePaths)
            For Each pair In runtimeOverrides
                _loader.RegisterRuntimePathOverride(pair.Key, pair.Value)
            Next

            ' Full managed runtime closure registration (design §G1): ScriptBuilder registers only the
            ' assemblies a submission binds at compile time, but framework-style scripts (e.g. the Avalonia
            ' demo) need closure assemblies that are reached only at runtime (base types, lazily touched
            ' deps) to be resolvable too. Register every restored lib asset with the loader so a later
            ' ResolveAssembly can satisfy those by simple name.
            RegisterRuntimeClosure(assets.RuntimePaths)
        End Sub

        ''' <summary>
        ''' Registers every restored managed runtime (lib) assembly with the loader (design §G1). Only paths
        ''' that exist on disk are read; unit-test fixtures use non-existent absolute paths and are skipped, so
        ''' this stays a no-op there.
        ''' </summary>
        Friend Sub RegisterRuntimeClosure(runtimePaths As ImmutableArray(Of String))
            If _loader Is Nothing OrElse runtimePaths.IsDefaultOrEmpty Then
                Return
            End If

            For Each runtimePath In runtimePaths
                If Not File.Exists(runtimePath) Then
                    Continue For
                End If

                Try
                    Dim loadedName = AssemblyName.GetAssemblyName(runtimePath)
                    Dim identity As AssemblyIdentity = Nothing
                    If loadedName IsNot Nothing AndAlso AssemblyIdentity.TryParseDisplayName(loadedName.FullName, identity) Then
                        _loader.RegisterDependency(identity, runtimePath)
                    End If
                Catch generatedExceptionName As Exception
                    ' Not a managed assembly or not readable; leave it to default resolution.
                End Try
            Next
        End Sub

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
