' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' No-side-effect tests for the F-A runtime asset handshake (design-detailed.md §G1/§G2, the reader pure
' ref->lib override table and the Scripting loader runtime-path override seam). None of these load or touch
' a file on disk: dependency registrations use fake absolute asset paths and the restore runner is a fake
' that owns its outcome, exactly like the coordinator D5-D8 tests.

Imports System.Collections.Immutable
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting
Imports Xunit

Public Class NuGetRuntimeHandshakeTests

    Private Const Quote As String = """"

    ' --- Loader runtime-path override seam (design §G1) ---

    Private Const RefPath As String = "C:/nuget/packages/contoso.main/2.0.0/ref/net10.0/Contoso.Main.dll"
    Private Const LibPath As String = "C:/nuget/packages/contoso.main/2.0.0/lib/net10.0/Contoso.Main.dll"

    <Fact>
    Public Sub LoaderRegisteredDependencyLocationKeepsPathWhenNoOverride()
        Dim loader As New InteractiveAssemblyLoader()
        Dim identity As New AssemblyIdentity("Contoso.Main", New Version(2, 0, 0, 0))

        loader.RegisterDependency(identity, LibPath)

        Dim locations = loader.GetRegisteredDependencyLocations("Contoso.Main")
        Assert.Equal(1, locations.Length)
        Assert.Equal(LibPath, locations(0))
    End Sub

    <Fact>
    Public Sub LoaderRuntimePathOverrideRedirectsRefAssetToRuntimeAsset()
        Dim loader As New InteractiveAssemblyLoader()
        Dim identity As New AssemblyIdentity("Contoso.Main", New Version(2, 0, 0, 0))

        loader.RegisterRuntimePathOverride(RefPath, LibPath)
        loader.RegisterDependency(identity, RefPath)

        Dim locations = loader.GetRegisteredDependencyLocations("Contoso.Main")
        Assert.Equal(1, locations.Length)
        Assert.Equal(LibPath, locations(0))
    End Sub

    <Fact>
    Public Sub LoaderRuntimePathOverrideDoesNotAffectUnrelatedAsset()
        Dim loader As New InteractiveAssemblyLoader()
        Dim identity As New AssemblyIdentity("Contoso.Main", New Version(2, 0, 0, 0))

        loader.RegisterRuntimePathOverride(RefPath, LibPath)
        Dim unrelated = "C:/nuget/packages/contoso.other/1.0.0/lib/net10.0/Contoso.Other.dll"
        loader.RegisterDependency(identity, unrelated)

        Dim locations = loader.GetRegisteredDependencyLocations("Contoso.Main")
        Assert.Equal(1, locations.Length)
        Assert.Equal(unrelated, locations(0))
    End Sub

    <Fact>
    Public Sub LoaderNativeProbeRootsDefaultEmptyAndDeduplicates()
        Dim loader As New InteractiveAssemblyLoader()

        Assert.True(loader.NativeProbeRoots.IsEmpty)

        loader.AddNativeProbeRoot("C:/nuget/packages/contoso.native/1.0.0/runtimes/win-x64/native")
        Assert.Equal(1, loader.NativeProbeRoots.Length)

        ' Same directory twice is a no-op (dedupe, design §G2).
        loader.AddNativeProbeRoot("C:/nuget/packages/contoso.native/1.0.0/runtimes/win-x64/native")
        Assert.Equal(1, loader.NativeProbeRoots.Length)
    End Sub

    ' --- Pure ref->lib override table (design §G1, NuGetRestoreAssetsReader.ComputeRuntimePathOverrides) ---

    Private Shared Function CompileMap(mainRef As Boolean) As ImmutableDictionary(Of String, ImmutableArray(Of String))
        Dim builder = ImmutableDictionary.CreateBuilder(Of String, ImmutableArray(Of String))(StringComparer.OrdinalIgnoreCase)
        If mainRef Then
            builder("contoso.main,2.0.0") = ImmutableArray.Create(RefPath, "C:/nuget/packages/contoso.dep/1.0.0/lib/net10.0/Contoso.Dep.dll")
        Else
            builder("contoso.main,2.0.0") = ImmutableArray.Create("C:/nuget/packages/contoso.dep/1.0.0/lib/net10.0/Contoso.Dep.dll")
        End If
        builder("contoso.dep,1.0.0") = ImmutableArray.Create("C:/nuget/packages/contoso.dep/1.0.0/lib/net10.0/Contoso.Dep.dll")
        Return builder.ToImmutable()
    End Function

    <Fact>
    Public Sub RuntimeOverridesMapRefAssetToLibAssetAndSkipsLibOnlyPackages()
        Dim runtimePaths = ImmutableArray.Create(
            LibPath,
            "C:/nuget/packages/contoso.dep/1.0.0/lib/net10.0/Contoso.Dep.dll")

        Dim runtimeOverrides = NuGetRestoreAssetsReader.ComputeRuntimePathOverrides(CompileMap(mainRef:=True), runtimePaths)

        Assert.Equal(1, runtimeOverrides.Count)
        Assert.True(runtimeOverrides.ContainsKey(RefPath))
        Assert.Equal(LibPath, runtimeOverrides(RefPath))
    End Sub

    <Fact>
    Public Sub RuntimeOverridesEmptyWhenNoRefLibSplit()
        Dim runtimePaths = ImmutableArray.Create("C:/nuget/packages/contoso.dep/1.0.0/lib/net10.0/Contoso.Dep.dll")

        Dim runtimeOverrides = NuGetRestoreAssetsReader.ComputeRuntimePathOverrides(CompileMap(mainRef:=False), runtimePaths)

        Assert.True(runtimeOverrides.IsEmpty)
    End Sub

    <Fact>
    Public Sub RuntimeOverridesSkipsAmbiguousRuntimeFileName()
        Dim ambiguousRuntime = ImmutableArray.Create(
            LibPath,
            "C:/other/contoso.main/2.0.0/lib/net10.0/Contoso.Main.dll")

        Dim runtimeOverrides = NuGetRestoreAssetsReader.ComputeRuntimePathOverrides(CompileMap(mainRef:=True), ambiguousRuntime)

        Assert.True(runtimeOverrides.IsEmpty)
    End Sub

    <Fact>
    Public Sub RuntimeOverridesEmptyInputsReturnEmpty()
        Assert.True(NuGetRestoreAssetsReader.ComputeRuntimePathOverrides(
            ImmutableDictionary(Of String, ImmutableArray(Of String)).Empty,
            ImmutableArray(Of String).Empty).IsEmpty)
    End Sub

    ' --- Coordinator pushes restored assets into a wired loader (design §G1/§G2) ---

    ' Mirrors the real project.assets.json shape the SDK writes: "packageFolders" is an object, the
    ' "targets" keys are the short target-framework moniker (optionally "/<rid>"), and "dependencies" uses
    ' bare package names. Contoso.Main is a ref/lib split package whose dependency Contoso.Dep is lib-only.
    Private Const HandshakeAssetsJson As String = "{""packageFolders"":{""C:/nuget/packages/"":{}},""libraries"":{""Contoso.Main/2.0.0"":{""type"":""package"",""path"":""contoso.main/2.0.0""},""Contoso.Dep/1.0.0"":{""type"":""package"",""path"":""contoso.dep/1.0.0""}},""targets"":{""net10.0"":{""Contoso.Main/2.0.0"":{""type"":""package"",""compile"":{""ref/net10.0/Contoso.Main.dll"":{}},""runtime"":{""lib/net10.0/Contoso.Main.dll"":{}},""dependencies"":{""Contoso.Dep"":{}}},""Contoso.Dep/1.0.0"":{""type"":""package"",""compile"":{""lib/net10.0/Contoso.Dep.dll"":{}},""runtime"":{""lib/net10.0/Contoso.Dep.dll"":{}}}},""net10.0/win-x64"":{""Contoso.Main/2.0.0"":{""type"":""package"",""runtime"":{""lib/net10.0/Contoso.Main.dll"":{}}},""Contoso.Dep/1.0.0"":{""type"":""package"",""runtime"":{""lib/net10.0/Contoso.Dep.dll"":{}}}}}}"

    <Fact>
    Public Sub CoordinatorAfterSuccessfulRestoreRegistersRuntimeOverrideOnLoader()
        Dim session As New NuGetPackageSession()
        Dim loader As New InteractiveAssemblyLoader()
        Dim fakeRunner As New FakeRestoreRunner()
        fakeRunner.Outcome = New RestoreOutcome(exitCode:=0, standardError:="", nuGetCacheWritten:=True, assetsJsonText:=HandshakeAssetsJson)
        Dim coordinator As New NuGetRestoreCoordinator(session, runner:=fakeRunner, loader:=loader)

        Dim code = "#R " & Quote & "nuget:Contoso.Main, 2.0.0" & Quote & vbCrLf & "? 1"
        Dim diagnostics = coordinator.PrepareCompilationAsync(SourceText.From(code), "", CancellationToken.None).GetAwaiter().GetResult()

        Assert.True(diagnostics.IsEmpty)
        Assert.Equal(1, fakeRunner.RestoreCalls)

        ' The coordinator restored into the session and handed the runtime override to the loader: a later
        ' ScriptBuilder registration of the compile (ref) asset is redirected to the runtime (lib) asset.
        Dim mainCompile = session.TryGetCompilePaths("Contoso.Main", "2.0.0")
        Assert.False(mainCompile.IsDefaultOrEmpty)
        Dim mainRefPath = mainCompile(0)
        Assert.EndsWith("/ref/net10.0/Contoso.Main.dll", mainRefPath, StringComparison.OrdinalIgnoreCase)

        loader.RegisterDependency(New AssemblyIdentity("Contoso.Main", New Version(2, 0, 0, 0)), mainRefPath)
        Dim locations = loader.GetRegisteredDependencyLocations("Contoso.Main")
        Assert.Equal(1, locations.Length)
        Assert.EndsWith("/lib/net10.0/Contoso.Main.dll", locations(0), StringComparison.OrdinalIgnoreCase)
        Assert.NotEqual(mainRefPath, locations(0))

        ' Pure-managed graph carries no native roots.
        Assert.True(loader.NativeProbeRoots.IsEmpty)
    End Sub

    <Fact>
    Public Sub CoordinatorWithoutLoaderSkipsPush()
        Dim session As New NuGetPackageSession()
        Dim fakeRunner As New FakeRestoreRunner()
        fakeRunner.Outcome = New RestoreOutcome(exitCode:=0, standardError:="", nuGetCacheWritten:=True, assetsJsonText:=HandshakeAssetsJson)
        Dim coordinator As New NuGetRestoreCoordinator(session, runner:=fakeRunner)

        Dim code = "#R " & Quote & "nuget:Contoso.Main, 2.0.0" & Quote & vbCrLf & "? 1"
        Dim diagnostics = coordinator.PrepareCompilationAsync(SourceText.From(code), "", CancellationToken.None).GetAwaiter().GetResult()

        Assert.True(diagnostics.IsEmpty)
        Assert.Equal(1, fakeRunner.RestoreCalls)
    End Sub

    ' --- Full runtime closure registration (design §G1) ---
    '
    ' ScriptBuilder registers only the assemblies a submission binds at compile time; framework-style
    ' scripts need closure assemblies that are reached only at runtime to be resolvable too. The coordinator
    ' therefore registers every restored runtime (lib) asset with the loader. These tests read the identity
    ' of an existing, already-loaded assembly (the compiler assembly in the test output directory) and skip
    ' non-existent fixture paths, so they stay side-effect free (no writes, no process, no network).

    <Fact>
    Public Sub CoordinatorRegistersExistingRuntimeClosureAssemblyOnLoader()
        Dim session As New NuGetPackageSession()
        Dim loader As New InteractiveAssemblyLoader()
        Dim fakeRunner As New FakeRestoreRunner()
        Dim coordinator As New NuGetRestoreCoordinator(session, runner:=fakeRunner, loader:=loader)

        Dim realPath = GetType(AssemblyIdentity).Assembly.Location
        Dim simpleName = Path.GetFileNameWithoutExtension(realPath)

        coordinator.RegisterRuntimeClosure(ImmutableArray.Create(realPath))

        Dim locations = loader.GetRegisteredDependencyLocations(simpleName)
        Assert.Contains(locations, Function(p) String.Equals(p, realPath, StringComparison.OrdinalIgnoreCase))
    End Sub

    <Fact>
    Public Sub CoordinatorRegisterRuntimeClosureSkipsMissingAndEmpty()
        Dim session As New NuGetPackageSession()
        Dim loader As New InteractiveAssemblyLoader()
        Dim fakeRunner As New FakeRestoreRunner()
        Dim coordinator As New NuGetRestoreCoordinator(session, runner:=fakeRunner, loader:=loader)

        ' Non-existent absolute fixture path is skipped without touching the loader.
        coordinator.RegisterRuntimeClosure(ImmutableArray.Create("C:/nuget/packages/contoso.none/1.0.0/lib/net8.0/Contoso.None.dll"))
        Assert.True(loader.GetRegisteredDependencyLocations("Contoso.None").IsEmpty)

        ' An empty runtime set is a no-op.
        coordinator.RegisterRuntimeClosure(ImmutableArray(Of String).Empty)
        Assert.True(loader.NativeProbeRoots.IsEmpty)
    End Sub

    ' --- Upward version unification (upstream-merge 2.15, design F-D) ---
    '
    ' Restored NuGet runtime assets can carry a higher assembly version than a dependency binary was
    ' compiled against (e.g. FluentAvaloniaUI preview2 references Avalonia 12.0.0.0 while the restored
    ' Avalonia 12.1.1 package ships 12.1.1.0). ResolveBestDefinitionIndex is the shared selection policy
    ' behind both FindHighestVersionOrFirstMatchingIdentity overloads: an exact (or platform-unified)
    ' candidate wins; otherwise the highest non-downgrading definition version is accepted. These tests
    ' lock that policy on fabricated strong-named identities only (no file or assembly load).

    Private Shared ReadOnly PublicKeyToken As ImmutableArray(Of Byte) =
        ImmutableArray.Create(Of Byte)(New Byte() {1, 2, 3, 4, 5, 6, 7, 8})
    Private Shared ReadOnly OtherPublicKeyToken As ImmutableArray(Of Byte) =
        ImmutableArray.Create(Of Byte)(New Byte() {9, 10, 11, 12, 13, 14, 15, 16})

    Private Shared Function StrongName(name As String, version As Version, pkt As ImmutableArray(Of Byte), Optional culture As String = Nothing) As AssemblyIdentity
        Return New AssemblyIdentity(name, version, culture, pkt)
    End Function

    <Fact>
    Public Sub LoaderUnifiesUpwardToHighestVersionWhenNoExactCandidate()
        Dim reference = StrongName("Contoso.Lib", New Version(1, 0, 0, 0), PublicKeyToken)
        Dim definitions As AssemblyIdentity() = {
            StrongName("Contoso.Lib", New Version(2, 0, 0, 0), PublicKeyToken),
            StrongName("Contoso.Lib", New Version(3, 0, 0, 0), PublicKeyToken)}

        Assert.Equal(1, InteractiveAssemblyLoader.ResolveBestDefinitionIndex(reference, definitions))
    End Sub

    <Fact>
    Public Sub LoaderPrefersExactVersionOverHigherDefinition()
        Dim reference = StrongName("Contoso.Lib", New Version(1, 0, 0, 0), PublicKeyToken)
        Dim definitions As AssemblyIdentity() = {
            StrongName("Contoso.Lib", New Version(1, 0, 0, 0), PublicKeyToken),
            StrongName("Contoso.Lib", New Version(2, 0, 0, 0), PublicKeyToken)}

        Assert.Equal(0, InteractiveAssemblyLoader.ResolveBestDefinitionIndex(reference, definitions))
    End Sub

    <Fact>
    Public Sub LoaderPrefersExactVersionEvenWhenHigherDefinitionAppearsFirst()
        Dim reference = StrongName("Contoso.Lib", New Version(1, 0, 0, 0), PublicKeyToken)
        Dim definitions As AssemblyIdentity() = {
            StrongName("Contoso.Lib", New Version(3, 0, 0, 0), PublicKeyToken),
            StrongName("Contoso.Lib", New Version(1, 0, 0, 0), PublicKeyToken)}

        Assert.Equal(1, InteractiveAssemblyLoader.ResolveBestDefinitionIndex(reference, definitions))
    End Sub

    <Fact>
    Public Sub LoaderRefusesDowngradeWhenReferenceAboveAllDefinitions()
        Dim reference = StrongName("Contoso.Lib", New Version(3, 0, 0, 0), PublicKeyToken)
        Dim definitions As AssemblyIdentity() = {
            StrongName("Contoso.Lib", New Version(1, 0, 0, 0), PublicKeyToken),
            StrongName("Contoso.Lib", New Version(2, 0, 0, 0), PublicKeyToken)}

        Assert.Equal(-1, InteractiveAssemblyLoader.ResolveBestDefinitionIndex(reference, definitions))
    End Sub

    <Fact>
    Public Sub LoaderReturnsNoCandidateForEmptyOrNonMatchingDefinitions()
        Dim reference = StrongName("Contoso.Lib", New Version(1, 0, 0, 0), PublicKeyToken)

        Assert.Equal(-1, InteractiveAssemblyLoader.ResolveBestDefinitionIndex(reference, Array.Empty(Of AssemblyIdentity)()))

        Dim wrongName As AssemblyIdentity() = {StrongName("Contoso.Other", New Version(2, 0, 0, 0), PublicKeyToken)}
        Assert.Equal(-1, InteractiveAssemblyLoader.ResolveBestDefinitionIndex(reference, wrongName))

        Dim wrongKey As AssemblyIdentity() = {StrongName("Contoso.Lib", New Version(2, 0, 0, 0), OtherPublicKeyToken)}
        Assert.Equal(-1, InteractiveAssemblyLoader.ResolveBestDefinitionIndex(reference, wrongKey))

        Dim wrongCulture As AssemblyIdentity() = {StrongName("Contoso.Lib", New Version(2, 0, 0, 0), PublicKeyToken, culture:="fr")}
        Assert.Equal(-1, InteractiveAssemblyLoader.ResolveBestDefinitionIndex(reference, wrongCulture))
    End Sub

    <Fact>
    Public Sub LoaderResolvesHigherVersionLoadedAssemblyForLowerVersionReference()
        Dim loader As New InteractiveAssemblyLoader()
        Dim realAssembly = GetType(AssemblyIdentity).Assembly
        loader.RegisterDependency(realAssembly)

        Dim asmName = realAssembly.GetName()
        Dim pktBytes = asmName.GetPublicKeyToken()
        Dim pkt = If(pktBytes, Array.Empty(Of Byte)()).ToImmutableArray()
        Dim version = asmName.Version
        Dim lowerVersion As New Version(Math.Max(0, version.Major - 1), 0, 0, 0)
        If lowerVersion.CompareTo(version) >= 0 Then
            lowerVersion = New Version(0, 0, 0, 0)
        End If
        Dim reference = New AssemblyIdentity(asmName.Name, lowerVersion, asmName.CultureName, pkt)

        Dim resolved = loader.ResolveAssembly(reference, Nothing)

        ' The loader returns the already-loaded (higher-version) assembly instead of failing to find an
        ' exact match, whether through the new strong-named upgrade path or the weak-name any-version path.
        Assert.Same(realAssembly, resolved)
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
End Class
