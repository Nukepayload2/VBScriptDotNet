' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' L3 tests for the NuGet three-layer seam (test-plan T-NUG-1..8, T-DI-3/T-DI-4). Every restore/resolver is a
' fake: no network, no child process, no file written, and the real restore path is not exercised here.

Imports System
Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.DependencyInjection.Extensions
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options
Imports Nukepayload2.VBScriptDotNet.Hosting
Imports Xunit

Public Class HostingNuGetTests

    ''' <summary>
    ''' T-NUG-1: a restore diagnostic blocks compilation. The result carries the restore diagnostic (not a compiler
    ''' diagnostic from the invalid script text), exits with 1 and does not throw.
    ''' </summary>
    <Fact>
    Public Async Function T_NUG_1_RestoreErrorBlocksCompilationAndBecomesResultDiagnostics() As Task
        Dim restore As New FakeNuGetRestoreService(ImmutableArray.Create(
            New VBScriptDiagnostic(
                "VBI1001",
                VBScriptDiagnosticSeverity.Error,
                "Package 'Pkg' could not be restored.",
                Nothing,
                1,
                1)))
        Dim resolver As New FakeNuGetPackageResolver(ImmutableArray(Of String).Empty)

        Using fixture As New RunnerFixture(afterRegistration:=Sub(services)
                                               services.Replace(ServiceDescriptor.Singleton(Of INuGetRestoreService)(restore))
                                               services.Replace(ServiceDescriptor.Singleton(Of INuGetPackageResolver)(resolver))
                                           End Sub)
            Dim result = Await fixture.Runner.RunCodeAsync("#R ""nuget:Pkg, 1.0""" & Environment.NewLine & "Dim x =")

            Assert.Equal(1, result.ExitCode)
            Assert.Equal(1, result.Diagnostics.Length)
            Assert.Equal("VBI1001", result.Diagnostics(0).Id)
            Assert.Equal(VBScriptDiagnosticSeverity.Error, result.Diagnostics(0).Severity)
            Assert.Equal(1, result.Diagnostics(0).Line)
            Assert.DoesNotContain(result.Diagnostics, Function(d) d.Id.StartsWith("BC", StringComparison.Ordinal))
            Assert.Null(result.Exception)
            Assert.Equal(1, restore.Calls)
        End Using
    End Function

    ''' <summary>
    ''' T-NUG-2: an empty restore result lets compilation continue; with no <c>#R "nuget:"</c> directive the resolver
    ''' is never consulted.
    ''' </summary>
    <Fact>
    Public Async Function T_NUG_2_EmptyRestoreLetsCompilationContinueWithoutCallingTheResolver() As Task
        Dim restore As New FakeNuGetRestoreService()
        Dim resolver As New FakeNuGetPackageResolver(ImmutableArray(Of String).Empty)

        Using fixture As New RunnerFixture(afterRegistration:=Sub(services)
                                               services.Replace(ServiceDescriptor.Singleton(Of INuGetRestoreService)(restore))
                                               services.Replace(ServiceDescriptor.Singleton(Of INuGetPackageResolver)(resolver))
                                           End Sub)
            Dim result = Await fixture.Runner.RunCodeAsync("Return 5")

            Assert.Equal(5, result.ExitCode)
            Assert.True(result.Diagnostics.IsEmpty)
            Assert.Equal(1, restore.Calls)
            Assert.Empty(resolver.Calls)
        End Using
    End Function

    ''' <summary>
    ''' T-NUG-3: <c>#R "nuget:Pkg, 1.0"</c> is resolved through the public resolver (here to an already loaded
    ''' assembly) and compilation succeeds.
    ''' </summary>
    <Fact>
    Public Async Function T_NUG_3_NuGetReferenceResolvesThroughThePublicResolver() As Task
        Dim restore As New FakeNuGetRestoreService()
        Dim resolver As New FakeNuGetPackageResolver(ImmutableArray.Create(GetType(HostingNuGetTests).Assembly.Location))

        Using fixture As New RunnerFixture(afterRegistration:=Sub(services)
                                               services.Replace(ServiceDescriptor.Singleton(Of INuGetRestoreService)(restore))
                                               services.Replace(ServiceDescriptor.Singleton(Of INuGetPackageResolver)(resolver))
                                           End Sub)
            Dim result = Await fixture.Runner.RunCodeAsync("#R ""nuget:Pkg, 1.0""" & Environment.NewLine & "Return 0")

            Assert.Equal(0, result.ExitCode)
            Assert.True(result.Diagnostics.IsEmpty, String.Join(" | ", result.Diagnostics))
            Assert.Null(result.Exception)
            Assert.Single(resolver.Calls)
            Assert.Equal("Pkg, 1.0", resolver.Calls(0))
            ' Printed so a run shows the actual end-to-end result of the fake-resolver case.
            Console.WriteLine(
                "T-NUG-3 actual: ExitCode=" & result.ExitCode &
                "; Diagnostics=" & result.Diagnostics.Length &
                "; Exception=" & If(result.Exception Is Nothing, "<none>", result.Exception.GetType().Name) &
                "; resolverCalls=" & String.Join("/", resolver.Calls))
        End Using
    End Function

    ''' <summary>
    ''' T-NUG-4: the restore context is exposed on the options with the host environment filled in; the SDK version
    ''' is documented as empty until a real restore observes one.
    ''' </summary>
    <Fact>
    Public Sub T_NUG_4_RestoreContextExposesTheHostEnvironment()
        Dim options As New VBScriptOptions()
        Dim context = options.RestoreContext

        Assert.NotNull(context)
        Assert.Same(context, options.RestoreContext)
        Assert.False(String.IsNullOrWhiteSpace(context.TargetFramework), "TargetFramework must be available")
        Assert.False(String.IsNullOrWhiteSpace(context.RuntimeIdentifier), "RuntimeIdentifier must be available")
        Assert.False(String.IsNullOrWhiteSpace(context.PlatformVersion), "PlatformVersion must be available")
        Assert.False(String.IsNullOrWhiteSpace(context.SourceFingerprint), "SourceFingerprint must never be empty")
        ' Availability-based: no restore has run for this options instance, so the SDK version is the empty string.
        Assert.NotNull(context.SdkVersion)

        ' The DI path exposes the same snapshot: it is not left as Nothing by AddVBScripting.
        Dim services As New ServiceCollection()
        services.AddVBScripting()
        Using provider = services.BuildServiceProvider()
            Dim diOptions = provider.GetRequiredService(Of IOptions(Of VBScriptOptions)).Value
            Assert.NotNull(diOptions.RestoreContext)
            Assert.False(String.IsNullOrWhiteSpace(diOptions.RestoreContext.SourceFingerprint))

            ' AddVBScripting registers the three defaults itself; the session is a singleton shared by the
            ' restore coordinator and the resolver.
            Assert.IsType(Of DotnetCliRestoreService)(provider.GetRequiredService(Of INuGetRestoreService)())
            Assert.IsType(Of SessionNuGetPackageResolver)(provider.GetRequiredService(Of INuGetPackageResolver)())
            Assert.IsType(Of DotNetRestoreRunnerAdapter)(provider.GetRequiredService(Of INuGetRestoreRunner)())
            Assert.Same(
                provider.GetRequiredService(Of NuGetPackageSession)(),
                provider.GetRequiredService(Of NuGetPackageSession)())
        End Using
    End Sub

    ''' <summary>
    ''' T-NUG-5: the default restore service writes exactly one Information entry (with the restore context) the
    ''' first time it is triggered, and does not log again on later triggers.
    ''' </summary>
    <Fact>
    Public Async Function T_NUG_5_DefaultRestoreServiceLogsOneInformationOnFirstTrigger() As Task
        Dim sink As New FakeLogSink()
        Dim scriptOptions As New VBScriptOptions()
        Dim service As New DotnetCliRestoreService(
            Microsoft.Extensions.Options.Options.Create(scriptOptions),
            New NuGetPackageSession(),
            New FakeLogger(Of DotnetCliRestoreService)(sink),
            New FakeNuGetRestoreRunner())

        Dim first = Await service.PrepareCompilationAsync(New VBScriptSource("Return 1"), scriptOptions, CancellationToken.None)
        Dim second = Await service.PrepareCompilationAsync(New VBScriptSource("Return 2"), scriptOptions, CancellationToken.None)

        Assert.True(first.IsEmpty)
        Assert.True(second.IsEmpty)

        Dim information = sink.Records.Where(Function(record) record.Level = LogLevel.Information).ToArray()
        Assert.Single(information)
        Assert.Contains("TargetFramework", information(0).Message)
        Assert.Contains(scriptOptions.RestoreContext.SourceFingerprint, information(0).Message)
    End Function

    ''' <summary>
    ''' T-DI-3: replacing only <see cref="INuGetRestoreService"/> leaves the default resolver reading a session the
    ''' custom service never fills, so constructing the runner fails with a host error that names the fix.
    ''' </summary>
    <Fact>
    Public Sub T_DI_3_ReplacingOnlyTheRestoreServiceFailsRunnerConstruction()
        Using fixture As New RunnerFixture(afterRegistration:=Sub(services)
                                               services.Replace(ServiceDescriptor.Singleton(Of INuGetRestoreService)(New FakeNuGetRestoreService()))
                                           End Sub)
            Dim failure As Exception = Nothing
            Try
                Dim ignored = fixture.Runner
            Catch ex As Exception
                failure = ex
            End Try

            Assert.NotNull(failure)
            Dim hostFailure = FindHostException(failure)
            Assert.NotNull(hostFailure)
            Assert.Contains("AddVBScriptNuGetRestore", hostFailure.Message)
            Assert.Contains("INuGetPackageResolver", hostFailure.Message)
        End Using
    End Sub

    ''' <summary>
    ''' T-DI-4: after <c>AddVBScriptNuGetRestore()</c> all three layers can be replaced together; the runner
    ''' constructs without error and every layer resolves to the custom implementation.
    ''' </summary>
    <Fact>
    Public Async Function T_DI_4_GroupRegistrationAllowsReplacingAllThreeLayers() As Task
        Dim restore As New FakeNuGetRestoreService()
        Dim resolver As New FakeNuGetPackageResolver(ImmutableArray.Create(GetType(HostingNuGetTests).Assembly.Location))
        Dim runnerLayer As New FakeNuGetRestoreRunner()

        Using fixture As New RunnerFixture(afterRegistration:=Sub(services)
                                               services.AddVBScriptNuGetRestore()
                                               services.Replace(ServiceDescriptor.Singleton(Of INuGetRestoreService)(restore))
                                               services.Replace(ServiceDescriptor.Singleton(Of INuGetPackageResolver)(resolver))
                                               services.Replace(ServiceDescriptor.Singleton(Of INuGetRestoreRunner)(runnerLayer))
                                           End Sub)
            Assert.Same(restore, fixture.Provider.GetRequiredService(Of INuGetRestoreService)())
            Assert.Same(resolver, fixture.Provider.GetRequiredService(Of INuGetPackageResolver)())
            Assert.Same(runnerLayer, fixture.Provider.GetRequiredService(Of INuGetRestoreRunner)())
            ' Group registration only replaces the NuGet layers; the runner and output registrations stay intact.
            Assert.IsType(Of VBScriptRunner)(fixture.Runner)

            ' Resolution is not enough: run a NuGet reference so the custom restore and resolver are actually
            ' exercised (the custom runner layer is exercised by T-NUG-6, where the default service is in place).
            Dim result = Await fixture.Runner.RunCodeAsync("#R ""nuget:Pkg, 1.0""" & Environment.NewLine & "Return 3")
            Assert.Equal(3, result.ExitCode)
            Assert.Equal(1, restore.Calls)
            Assert.Single(resolver.Calls)
        End Using
    End Function

    ''' <summary>
    ''' The public contract has no loader, but the default implementation's Friend capability does. The execution
    ''' adapter must detect it and pass the per-call loader (this is what lets a real restore register runtime
    ''' assets before the script runs).
    ''' </summary>
    <Fact>
    Public Async Function Supplementary_LoaderAwareRestoreServiceReceivesThePerCallLoader() As Task
        Dim restore As New FakeLoaderAwareRestoreService()
        Dim resolver As New FakeNuGetPackageResolver(ImmutableArray(Of String).Empty)

        Using fixture As New RunnerFixture(afterRegistration:=Sub(services)
                                               services.Replace(ServiceDescriptor.Singleton(Of INuGetRestoreService)(restore))
                                               services.Replace(ServiceDescriptor.Singleton(Of INuGetPackageResolver)(resolver))
                                           End Sub)
            Dim result = Await fixture.Runner.RunCodeAsync("Return 1")

            Assert.Equal(1, result.ExitCode)
            Assert.Equal(1, restore.LoaderCalls)
            Assert.Equal(0, restore.PublicCalls)
            Assert.NotNull(restore.LoaderSeen)
        End Using
    End Function

    ''' <summary>
    ''' The default service learns the SDK version from the first real restore (through the coordinator) and records
    ''' it in the shared <see cref="NuGetRestoreContext.SdkVersion"/>.
    ''' </summary>
    <Fact>
    Public Async Function Supplementary_DefaultRestoreServiceRecordsTheSdkVersionObservedByARestore() As Task
        Dim sink As New FakeLogSink()
        Dim scriptOptions As New VBScriptOptions()
        Dim service As New DotnetCliRestoreService(
            Microsoft.Extensions.Options.Options.Create(scriptOptions),
            New NuGetPackageSession(),
            New FakeLogger(Of DotnetCliRestoreService)(sink),
            New FakeNuGetRestoreRunner(
                ImmutableArray.Create("10.0.100"),
                New NuGetRestoreOutcome(1, "simulated restore failure", nuGetCacheWritten:=False, assetsJsonText:=Nothing)))

        Dim diagnostics = Await service.PrepareCompilationAsync(
            New VBScriptSource("#R ""nuget:Pkg, 1.0"""),
            scriptOptions,
            CancellationToken.None)

        ' The fake executor fails the restore, which is enough to observe the SDK selection and produce a diagnostic.
        Assert.False(diagnostics.IsEmpty)
        Assert.Equal("10.0.100", scriptOptions.RestoreContext.SdkVersion)

        Dim information = sink.Records.Where(Function(record) record.Level = LogLevel.Information).ToArray()
        Assert.Single(information)
        Assert.Contains("10.0.100", information(0).Message)
    End Function

    ''' <summary>
    ''' T-NUG-6: replacing only <see cref="INuGetRestoreRunner"/> must be effective. The default
    ''' restore service is still in place, but the restore it requests is executed by the custom public runner.
    ''' </summary>
    <Fact>
    Public Async Function T_NUG_6_ReplacingOnlyThePublicRestoreRunnerIsActuallyUsed() As Task
        Dim publicRunner As New FakeNuGetRestoreRunner(
            ImmutableArray.Create("10.0.100"),
            New NuGetRestoreOutcome(1, "simulated restore failure", nuGetCacheWritten:=False, assetsJsonText:=Nothing))

        Using fixture As New RunnerFixture(afterRegistration:=Sub(services)
                                               services.Replace(ServiceDescriptor.Singleton(Of INuGetRestoreRunner)(publicRunner))
                                           End Sub)
            ' Restore coordination and package resolution are still the defaults; only the executor changed.
            Assert.IsType(Of DotnetCliRestoreService)(fixture.Provider.GetRequiredService(Of INuGetRestoreService)())
            Assert.IsType(Of SessionNuGetPackageResolver)(fixture.Provider.GetRequiredService(Of INuGetPackageResolver)())

            Dim result = Await fixture.Runner.RunCodeAsync("#R ""nuget:Pkg, 1.0""")

            Assert.Equal(1, publicRunner.Calls)
            Assert.Equal(1, publicRunner.SdkCalls)
            Assert.Equal(1, result.ExitCode)
            Assert.Null(result.Exception)
        End Using
    End Function

    ''' <summary>
    ''' T-NUG-7: the default service pins the coordinator's source-config fingerprint to
    ''' <see cref="VBScriptOptions.RestoreContext"/>'s value (so the cache key and the exposed context cannot drift),
    ''' and the public runner receives the package identity and mirror request fields.
    ''' </summary>
    <Fact>
    Public Async Function T_NUG_7_RestoreContextSourceFingerprintReachesTheCoordinator() As Task
        Dim sink As New FakeLogSink()
        Dim scriptOptions As New VBScriptOptions()
        Dim publicRunner As New FakeNuGetRestoreRunner(
            ImmutableArray.Create("10.0.100"),
            New NuGetRestoreOutcome(1, "simulated restore failure", nuGetCacheWritten:=False, assetsJsonText:=Nothing))
        Dim service As New DotnetCliRestoreService(
            Microsoft.Extensions.Options.Options.Create(scriptOptions),
            New NuGetPackageSession(),
            New FakeLogger(Of DotnetCliRestoreService)(sink),
            publicRunner)

        Dim diagnostics = Await service.PrepareCompilationAsync(
            New VBScriptSource("#R ""nuget:Pkg, 1.0"""),
            scriptOptions,
            CancellationToken.None)

        Assert.False(diagnostics.IsEmpty)
        Assert.NotNull(publicRunner.LastRequest)
        Assert.Equal("Pkg", publicRunner.LastRequest.PackageName)
        Assert.Equal("1.0", publicRunner.LastRequest.PackageVersion)
        Assert.False(String.IsNullOrWhiteSpace(publicRunner.LastRequest.SourceFingerprint))
        Assert.Equal(scriptOptions.RestoreContext.SourceFingerprint, publicRunner.LastRequest.SourceFingerprint)
    End Function

    ''' <summary>
    ''' T-NUG-8: the default <see cref="INuGetRestoreRunner"/> adapter mirrors every public request field into the
    ''' internal <c>RestoreRequest</c> the executor consumes (package identity, cache/project/global.json content,
    ''' project-relative path, the config mirror and the source fingerprint), and passes the raw outcome back
    ''' unchanged. The internal executor is a fake, so no process is spawned and no file is written.
    ''' </summary>
    <Fact>
    Public Async Function T_NUG_8_AdapterMapsPublicRequestFieldsToInternalRequest() As Task
        Dim internalRunner As New FakeInternalRestoreRunner()
        Dim adapter As New DotNetRestoreRunnerAdapter(internalRunner)
        Dim mirror = ImmutableArray.Create(
            New NuGetRestoreConfigFile(Path.Combine("sub", "NuGet.config"), ImmutableArray.Create(Of Byte)(1, 2, 3)))
        Dim projectRelativePath = Path.Combine("app", "project.vbproj")
        Dim request As New NuGetRestoreRequest(
            "Pkg",
            "1.2.3",
            cacheDirectory:="cache-root",
            projectXml:="<Project Sdk=""Microsoft.NET.Sdk"" />",
            globalJson:="{""sdk"": {""version"": ""10.0.100""}}",
            projectRelativePath:=projectRelativePath,
            mirrorFiles:=mirror,
            sourceFingerprint:="source-fingerprint-42")

        Dim outcome = Await adapter.RestoreAsync(request, CancellationToken.None)

        Dim mapped = internalRunner.LastRequest
        Assert.NotNull(mapped)
        Assert.Equal("cache-root", mapped.CacheDirectory)
        Assert.Equal("<Project Sdk=""Microsoft.NET.Sdk"" />", mapped.ProjectXml)
        Assert.Equal("{""sdk"": {""version"": ""10.0.100""}}", mapped.GlobalJson)
        Assert.Equal(projectRelativePath, mapped.ProjectRelativePath)
        Assert.Equal(Of String)(New String() {"Pkg"}, mapped.PackageNames)
        Assert.Equal(Of String)(New String() {"1.2.3"}, mapped.PackageVersions)
        Assert.Equal("source-fingerprint-42", mapped.SourceFingerprint)
        Assert.Single(mapped.MirrorFiles)
        Assert.Equal(Path.Combine("sub", "NuGet.config"), mapped.MirrorFiles(0).MirrorRelativePath)
        Assert.Equal(New Byte() {1, 2, 3}, mapped.MirrorFiles(0).Content)

        ' The raw outcome is forwarded, not reinterpreted.
        Assert.Equal(7, outcome.ExitCode)
        Assert.Equal("restore stderr", outcome.StandardError)
        Assert.True(outcome.NuGetCacheWritten)
        Assert.Equal("{""assets"": true}", outcome.AssetsJsonText)

        ' Empty optional fields are normalized to the internal defaults (empty arrays, default project file name).
        Dim minimal As New NuGetRestoreRequest("Pkg", "1.0", cacheDirectory:="cache-root", projectXml:=Nothing, globalJson:=Nothing)
        internalRunner.LastRequest = Nothing
        Await adapter.RestoreAsync(minimal, CancellationToken.None)
        Assert.NotNull(internalRunner.LastRequest)
        Assert.Equal(Of String)(New String() {"Pkg"}, internalRunner.LastRequest.PackageNames)
        Assert.Equal(Of String)(New String() {"1.0"}, internalRunner.LastRequest.PackageVersions)
        Assert.Equal(Path.Combine("cache-root", "project.vbproj"), internalRunner.LastRequest.ProjectFilePath)
        Assert.True(internalRunner.LastRequest.MirrorFiles.IsEmpty)
        Assert.Equal(String.Empty, internalRunner.LastRequest.SourceFingerprint)
    End Function

    Private Shared Function FindHostException(failure As Exception) As VBScriptHostException
        Dim current = failure
        While current IsNot Nothing
            Dim hostFailure = TryCast(current, VBScriptHostException)
            If hostFailure IsNot Nothing Then
                Return hostFailure
            End If

            current = current.InnerException
        End While

        Return Nothing
    End Function

End Class

''' <summary>A restore service that returns a fixed diagnostic set and counts its calls.</summary>
Friend NotInheritable Class FakeNuGetRestoreService
    Implements INuGetRestoreService

    Private ReadOnly _diagnostics As ImmutableArray(Of VBScriptDiagnostic)
    Private _calls As Integer

    Friend Sub New(Optional diagnostics As ImmutableArray(Of VBScriptDiagnostic) = Nothing)
        _diagnostics = If(diagnostics.IsDefault, ImmutableArray(Of VBScriptDiagnostic).Empty, diagnostics)
    End Sub

    Friend ReadOnly Property Calls As Integer
        Get
            Return _calls
        End Get
    End Property

    Public Function PrepareCompilationAsync(source As VBScriptSource, options As VBScriptOptions, cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of VBScriptDiagnostic)) Implements INuGetRestoreService.PrepareCompilationAsync
        Interlocked.Increment(_calls)
        Return Task.FromResult(_diagnostics)
    End Function

End Class

''' <summary>A package resolver that returns fixed paths and records the requested identities.</summary>
Friend NotInheritable Class FakeNuGetPackageResolver
    Implements INuGetPackageResolver

    Private ReadOnly _paths As ImmutableArray(Of String)
    Private ReadOnly _calls As New List(Of String)()

    Friend Sub New(paths As ImmutableArray(Of String))
        _paths = If(paths.IsDefault, ImmutableArray(Of String).Empty, paths)
    End Sub

    Friend ReadOnly Property Calls As IReadOnlyList(Of String)
        Get
            Return _calls.ToArray()
        End Get
    End Property

    Public Function Resolve(packageName As String, packageVersion As String) As ImmutableArray(Of String) Implements INuGetPackageResolver.Resolve
        _calls.Add(packageName & ", " & packageVersion)
        Return _paths
    End Function

End Class

''' <summary>
''' A restore-execution layer that answers the SDK probe and the restore without spawning a process or writing a
''' file. Counts both calls and records the last public request.
''' </summary>
Friend NotInheritable Class FakeNuGetRestoreRunner
    Implements INuGetRestoreRunner

    Private ReadOnly _sdkVersions As ImmutableArray(Of String)
    Private ReadOnly _outcome As NuGetRestoreOutcome
    Private _calls As Integer
    Private _sdkCalls As Integer
    Private _lastRequest As NuGetRestoreRequest

    Friend Sub New(Optional sdkVersions As ImmutableArray(Of String) = Nothing, Optional outcome As NuGetRestoreOutcome = Nothing)
        _sdkVersions = If(sdkVersions.IsDefault, ImmutableArray.Create("10.0.100"), sdkVersions)
        _outcome = If(outcome, New NuGetRestoreOutcome(0, String.Empty, nuGetCacheWritten:=True, assetsJsonText:="{}"))
    End Sub

    Friend ReadOnly Property Calls As Integer
        Get
            Return _calls
        End Get
    End Property

    Friend ReadOnly Property SdkCalls As Integer
        Get
            Return _sdkCalls
        End Get
    End Property

    Friend ReadOnly Property LastRequest As NuGetRestoreRequest
        Get
            Return _lastRequest
        End Get
    End Property

    Public Function GetInstalledSdkVersionsAsync(cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of String)) Implements INuGetRestoreRunner.GetInstalledSdkVersionsAsync
        Interlocked.Increment(_sdkCalls)
        Return Task.FromResult(_sdkVersions)
    End Function

    Public Function RestoreAsync(request As NuGetRestoreRequest, cancellationToken As CancellationToken) As Task(Of NuGetRestoreOutcome) Implements INuGetRestoreRunner.RestoreAsync
        _lastRequest = request
        Interlocked.Increment(_calls)
        Return Task.FromResult(_outcome)
    End Function

End Class

''' <summary>
''' The internal restore executor injected into the adapter by the field-mapping test: records the mapped request and
''' returns a fixed raw outcome, so the mapping is asserted without a process or a file.
''' </summary>
Friend NotInheritable Class FakeInternalRestoreRunner
    Implements IRestoreRunner

    Private _lastRequest As RestoreRequest

    Friend Property LastRequest As RestoreRequest
        Get
            Return _lastRequest
        End Get
        Set(value As RestoreRequest)
            _lastRequest = value
        End Set
    End Property

    Public Function GetInstalledSdkVersionsAsync(cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of String)) Implements IRestoreRunner.GetInstalledSdkVersionsAsync
        Return Task.FromResult(ImmutableArray.Create("10.0.100"))
    End Function

    Public Function RestoreAsync(request As RestoreRequest, cancellationToken As CancellationToken) As Task(Of RestoreOutcome) Implements IRestoreRunner.RestoreAsync
        _lastRequest = request
        Return Task.FromResult(New RestoreOutcome(7, "restore stderr", nuGetCacheWritten:=True, assetsJsonText:="{""assets"": true}"))
    End Function

End Class

''' <summary>
''' A restore service that implements the Friend loader capability, so it can prove the execution adapter prefers
''' the loader-aware overload over the public one.
''' </summary>
Friend NotInheritable Class FakeLoaderAwareRestoreService
    Implements INuGetRestoreService
    Implements INuGetRestoreServiceWithLoader

    Private _publicCalls As Integer
    Private _loaderCalls As Integer
    Private _loaderSeen As InteractiveAssemblyLoader

    Friend ReadOnly Property PublicCalls As Integer
        Get
            Return _publicCalls
        End Get
    End Property

    Friend ReadOnly Property LoaderCalls As Integer
        Get
            Return _loaderCalls
        End Get
    End Property

    Friend ReadOnly Property LoaderSeen As InteractiveAssemblyLoader
        Get
            Return _loaderSeen
        End Get
    End Property

    Public Function PrepareCompilationAsync(source As VBScriptSource, options As VBScriptOptions, cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of VBScriptDiagnostic)) Implements INuGetRestoreService.PrepareCompilationAsync
        Interlocked.Increment(_publicCalls)
        Return Task.FromResult(ImmutableArray(Of VBScriptDiagnostic).Empty)
    End Function

    Friend Function PrepareCompilationAsync(source As VBScriptSource, options As VBScriptOptions, loader As InteractiveAssemblyLoader, cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of VBScriptDiagnostic)) Implements INuGetRestoreServiceWithLoader.PrepareCompilationAsync
        Interlocked.Increment(_loaderCalls)
        _loaderSeen = loader
        Return Task.FromResult(ImmutableArray(Of VBScriptDiagnostic).Empty)
    End Function

End Class




