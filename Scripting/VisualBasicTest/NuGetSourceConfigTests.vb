' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' No-side-effect tests for the script-directory NuGet.config chain (design §C1–§C5, test-plan L1/L3/L4).
' L1 covers the pure collection layout and the fingerprint against an in-memory file system, L3 the
' RestoreRequest layout, L4 the coordinator wiring through a fake runner. No process is spawned, no network
' is touched, and the only disk writes are the mirror-write contract test's CreateIsolatedTempDirectory.
' The real end-to-end restore (that NuGet actually walks the mirrored chain) is the G manual checklist.

Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting
Imports Xunit

Public Class NuGetSourceConfigTests

    Private Const Quote As String = """"

    ' Minimal assets text shaped like a real obj/project.assets.json for one managed package (same shape the
    ' coordinator tests use), so a successful fake restore can write the session without a real restore.
    Private Const ManagedAssetsJson As String = "{""packageFolders"":{""C:/nuget/packages/"":{}},""libraries"":{""Contoso.Lib/2.0.0"":{""type"":""package"",""path"":""contoso.lib/2.0.0""}},""targets"":{""net10.0"":{""Contoso.Lib/2.0.0"":{""type"":""package"",""compile"":{""lib/net10.0/Contoso.Lib.dll"":{}},""runtime"":{""lib/net10.0/Contoso.Lib.dll"":{}}}}}}"

    Private Shared Function NuGetReference() As String
        Return "#R " & Quote & "nuget:Contoso.Lib, 2.0.0" & Quote & vbCrLf & "? 1"
    End Function

    ''' <summary>
    ''' Synthetic script tree root. It is never inspected on disk: every test answers the probe through an
    ''' injected <see cref="ISourceConfigFileSystem"/>, and the path only has to be absolute and platform
    ''' shaped so <c>Path</c> arithmetic behaves as it does in production.
    ''' </summary>
    Private Shared Function SourceRoot() As String
        Return Path.Combine(Path.GetTempPath(), "vbi-source-tree")
    End Function

    Private Shared Function ScriptDirectory() As String
        Return Path.Combine(SourceRoot(), "app")
    End Function

    Private Shared Function ScriptPath() As String
        Return Path.Combine(ScriptDirectory(), "main.vbx")
    End Function

    Private Shared Function CreateIsolatedTempDirectory() As String
        Dim directory = Path.Combine(AppContext.BaseDirectory, "TestTemp", Guid.NewGuid().ToString("N"))
        ' Fully qualified: the local shadows the Directory type (VB is case-insensitive).
        System.IO.Directory.CreateDirectory(directory)
        Return directory
    End Function

    ''' <summary>
    ''' In-memory <see cref="ISourceConfigFileSystem"/>: the directory chain need not exist on disk, so the
    ''' walk (termination at the volume root, one file per level, case-insensitive name matching) is exercised
    ''' without touching the file system.
    ''' </summary>
    Private NotInheritable Class MemoryFileSystem
        Implements ISourceConfigFileSystem

        ''' <summary>When True every directory "exists", so the walk reaches the volume root.</summary>
        Public Property AllDirectoriesExist As Boolean = True

        Public ReadOnly Directories As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Public ReadOnly Files As New Dictionary(Of String, Byte())(StringComparer.OrdinalIgnoreCase)

        ''' <summary>Path whose read throws, to exercise the unreadable-config diagnostic (design §C5).</summary>
        Public Property FailOnRead As String = Nothing

        Public Sub AddDirectory(path As String)
            Directories.Add(path)
        End Sub

        Public Sub AddFile(path As String, content As String)
            Files(path) = Encoding.UTF8.GetBytes(content)
        End Sub

        Public Function DirectoryExists(path As String) As Boolean Implements ISourceConfigFileSystem.DirectoryExists
            Return AllDirectoriesExist OrElse Directories.Contains(path)
        End Function

        Public Function EnumerateFiles(directory As String) As IEnumerable(Of String) Implements ISourceConfigFileSystem.EnumerateFiles
            Dim result As New List(Of String)()
            For Each candidate In Files.Keys
                If String.Equals(Path.GetDirectoryName(candidate), directory, StringComparison.OrdinalIgnoreCase) Then
                    result.Add(candidate)
                End If
            Next
            Return result
        End Function

        Public Function ReadAllBytes(path As String) As Byte() Implements ISourceConfigFileSystem.ReadAllBytes
            If FailOnRead IsNot Nothing AndAlso String.Equals(path, FailOnRead, StringComparison.OrdinalIgnoreCase) Then
                Throw New IOException("access denied (test double)")
            End If

            Dim content As Byte() = Nothing
            If Files.TryGetValue(path, content) Then
                Return content
            End If
            Throw New FileNotFoundException(path)
        End Function
    End Class

    ' --- L1: collection layout (design §C1 rules 1-4, C4) ---

    <Fact>
    Public Sub L1_1_NoConfigAnywhereYieldsEmptyCollectionAndScriptMirrorRoot()
        Dim fileSystem As New MemoryFileSystem()
        Dim script = ScriptPath()

        Dim result = NuGetSourceConfig.Collect(script, fileSystem)

        Assert.Equal(0, result.Files.Length)
        Assert.False(result.HasFailure)
        Assert.Equal(ScriptDirectory(), result.MirrorRoot)
        Assert.Equal("project.vbproj", NuGetSourceConfig.ProjectRelativePath(script, result.MirrorRoot))
    End Sub

    <Fact>
    Public Sub L1_2_ConfigBesideTheScriptKeepsTheProjectAtTheCacheRoot()
        Dim fileSystem As New MemoryFileSystem()
        Dim script = ScriptPath()
        fileSystem.AddFile(Path.Combine(ScriptDirectory(), "NuGet.config"), "<configuration/>")

        Dim result = NuGetSourceConfig.Collect(script, fileSystem)

        Assert.Equal(1, result.Files.Length)
        Assert.Equal("NuGet.config", result.Files(0).MirrorRelativePath)
        Assert.Equal(ScriptDirectory(), result.MirrorRoot)
        Assert.Equal("project.vbproj", NuGetSourceConfig.ProjectRelativePath(script, result.MirrorRoot))
    End Sub

    <Fact>
    Public Sub L1_3_TwoLevelsAreMirroredWithTheirRelativeDepth()
        Dim fileSystem As New MemoryFileSystem()
        Dim root = SourceRoot()
        Dim app = Path.Combine(root, "sub", "app")
        Dim script = Path.Combine(app, "main.vbx")
        fileSystem.AddFile(Path.Combine(root, "NuGet.config"), "root")
        fileSystem.AddFile(Path.Combine(root, "sub", "NuGet.config"), "sub")

        Dim result = NuGetSourceConfig.Collect(script, fileSystem)

        ' Sorted by mirror relative path: the topmost config first, so the mirror reproduces the whole chain.
        Assert.Equal(2, result.Files.Length)
        Assert.Equal("NuGet.config", result.Files(0).MirrorRelativePath)
        Assert.Equal(Path.Combine("sub", "NuGet.config"), result.Files(1).MirrorRelativePath)
        Assert.Equal("root", Encoding.UTF8.GetString(result.Files(0).Content))
        Assert.Equal("sub", Encoding.UTF8.GetString(result.Files(1).Content))
        Assert.Equal(root, result.MirrorRoot)
        Assert.Equal(Path.Combine("sub", "app", "project.vbproj"), NuGetSourceConfig.ProjectRelativePath(script, result.MirrorRoot))
    End Sub

    <Fact>
    Public Sub L1_4_ConfigNameIsMatchedCaseInsensitivelyAndMirroredCanonically()
        Dim fileSystem As New MemoryFileSystem()
        Dim script = ScriptPath()
        fileSystem.AddFile(Path.Combine(ScriptDirectory(), "nuget.config"), "lower")

        Dim result = NuGetSourceConfig.Collect(script, fileSystem)

        Assert.Equal(1, result.Files.Length)
        ' Mirrored under the canonical spelling, which is the one NuGet's own discovery looks for.
        Assert.Equal("NuGet.config", result.Files(0).MirrorRelativePath)
    End Sub

    <Fact>
    Public Sub L1_5_FingerprintChangesWithContent()
        Dim first = ImmutableArray.Create(New SourceConfigFile("NuGet.config", Encoding.UTF8.GetBytes("one")))
        Dim second = ImmutableArray.Create(New SourceConfigFile("NuGet.config", Encoding.UTF8.GetBytes("two")))

        Assert.NotEqual(NuGetSourceConfig.Fingerprint(first), NuGetSourceConfig.Fingerprint(second))
        Assert.Equal(64, NuGetSourceConfig.Fingerprint(first).Length)
    End Sub

    <Fact>
    Public Sub L1_6_FingerprintIsStableAcrossOrderAndPathCasing()
        Dim outer = New SourceConfigFile("NuGet.config", Encoding.UTF8.GetBytes("root"))
        Dim nested = New SourceConfigFile(Path.Combine("sub", "NuGet.config"), Encoding.UTF8.GetBytes("sub"))

        Assert.Equal(
            NuGetSourceConfig.Fingerprint(ImmutableArray.Create(outer, nested)),
            NuGetSourceConfig.Fingerprint(ImmutableArray.Create(nested, outer)))

        ' Only relative paths and bytes are consumed, lower-cased: a spelling difference is not an input change.
        Assert.Equal(
            NuGetSourceConfig.Fingerprint(ImmutableArray.Create(New SourceConfigFile("NUGET.CONFIG", Encoding.UTF8.GetBytes("root")))),
            NuGetSourceConfig.Fingerprint(ImmutableArray.Create(New SourceConfigFile("nuget.config", Encoding.UTF8.GetBytes("root")))))
    End Sub

    <Fact>
    Public Sub L1_7_EmptyCollectionFingerprintsAsEmptyString()
        ' The regression anchor for the no-config case: production used to pass a constant "" and must keep
        ' doing so when the script's directory chain carries no configuration.
        Assert.Equal("", NuGetSourceConfig.Fingerprint(ImmutableArray(Of SourceConfigFile).Empty))
        Assert.Equal("", NuGetSourceConfig.Fingerprint(Nothing))
    End Sub

    <Fact>
    Public Sub L1_8_BlankOrMissingScriptDirectoryYieldsEmptyCollection()
        Dim fileSystem As New MemoryFileSystem()

        Assert.Equal(0, NuGetSourceConfig.Collect(Nothing, fileSystem).Files.Length)
        Assert.Equal(0, NuGetSourceConfig.Collect("", fileSystem).Files.Length)
        Assert.Equal(0, NuGetSourceConfig.Collect("   ", fileSystem).Files.Length)

        ' A script path whose directory does not exist (the pre-existing tests' "/mem/main.vbx" case) is the
        ' same no-op, which is what keeps those tests' behavior unchanged.
        Dim missing As New MemoryFileSystem()
        missing.AllDirectoriesExist = False
        Dim result = NuGetSourceConfig.Collect(Path.Combine(SourceRoot(), "gone", "main.vbx"), missing)
        Assert.Equal(0, result.Files.Length)
        Assert.False(result.HasFailure)
    End Sub

    <Fact>
    Public Sub L1_9_WalkTerminatesAtTheVolumeRoot()
        Dim fileSystem As New MemoryFileSystem()
        Dim volumeRoot = Path.GetPathRoot(Path.GetTempPath())
        Assert.False(String.IsNullOrEmpty(volumeRoot))

        ' The script sits directly in the volume root: the upward walk must stop without throwing.
        Dim result = NuGetSourceConfig.Collect(Path.Combine(volumeRoot, "main.vbx"), fileSystem)

        Assert.Equal(0, result.Files.Length)
        Assert.Equal(volumeRoot, result.MirrorRoot)
        Assert.Equal("project.vbproj", NuGetSourceConfig.ProjectRelativePath(Path.Combine(volumeRoot, "main.vbx"), result.MirrorRoot))
    End Sub

    <Fact>
    Public Sub L1_10_DeeplyNestedChainIsCollectedInFull()
        Dim fileSystem As New MemoryFileSystem()
        Dim directory = SourceRoot()
        Dim expected As New List(Of String)()

        ' Eight levels, each carrying a config: the deepest (closest to the script) is collected last.
        For level = 0 To 7
            fileSystem.AddFile(Path.Combine(directory, "NuGet.config"), "level-" & level.ToString())
            expected.Insert(0, "level-" & level.ToString())
            directory = Path.Combine(directory, "d" & level.ToString())
        Next

        Dim script = Path.Combine(directory, "main.vbx")
        Dim result = NuGetSourceConfig.Collect(script, fileSystem)

        Assert.Equal(8, result.Files.Length)
        Assert.Equal(SourceRoot(), result.MirrorRoot)
        For index = 0 To 7
            Assert.Equal(expected(index), Encoding.UTF8.GetString(result.Files(index).Content))
        Next
        ' The project lands at the script's own depth below the mirror root.
        Assert.Equal(
            Path.Combine("d0", "d1", "d2", "d3", "d4", "d5", "d6", "d7", "project.vbproj"),
            NuGetSourceConfig.ProjectRelativePath(script, result.MirrorRoot))
    End Sub

    <Fact>
    Public Sub L1_11_UnreadableConfigIsReportedInsteadOfThrown()
        Dim fileSystem As New MemoryFileSystem()
        Dim script = ScriptPath()
        Dim configPath = Path.Combine(ScriptDirectory(), "NuGet.config")
        fileSystem.AddFile(configPath, "content")
        fileSystem.FailOnRead = configPath

        Dim result = NuGetSourceConfig.Collect(script, fileSystem)

        Assert.True(result.HasFailure)
        Assert.Equal(configPath, result.FailedPath)
        Assert.Contains("access denied", result.FailureReason)
    End Sub

    ' --- L1: cache key sensitivity (design §C3, §C3.1) ---

    <Fact>
    Public Sub L1_12_13_14_EmptyFingerprintKeepsThePreExistingCacheKey()
        Dim packages = ImmutableArray.Create("a,1.0.0", "b,2.0.0")
        Dim withoutConfig = NuGetRestoreCache.ComputeKey(packages, ".NETCoreApp,Version=v10.0", "win-x64", "10.0.100", "", net48:=False)

        ' L1-11: the source set is part of the key, so a changed configuration selects a different directory
        ' instead of silently reusing files that WriteOnce will never rewrite.
        Assert.NotEqual(withoutConfig, NuGetRestoreCache.ComputeKey(packages, ".NETCoreApp,Version=v10.0", "win-x64", "10.0.100", "abcd", net48:=False))

        ' L1-12: same fingerprint, same key.
        Assert.Equal(withoutConfig, NuGetRestoreCache.ComputeKey(packages, ".NETCoreApp,Version=v10.0", "win-x64", "10.0.100", "", net48:=False))

        ' L1-13: the descriptor shape is a frozen contract — every package key is followed by ";", then the
        ' long-form TFM, RID, SDK version, the fingerprint slot and the host flavor. This is the exact
        ' descriptor that was reproduced byte-for-byte from the real cache directory name of the
        ' microsoft.data.sqlite 8.0.1 restore recorded in design §C3.1, so an empty fingerprint must keep
        ' hashing to that same value.
        Assert.Equal(
            "643d9e5be49b2ba3d1a2c4a9fa68c386ee6e07a521d9dc1e684998604f4c9ca1",
            NuGetRestoreCache.ComputeKey(ImmutableArray.Create("microsoft.data.sqlite,8.0.1"), ".NETCoreApp,Version=v10.0", "win-x64", "10.0.401", "", net48:=False))
    End Sub

    ' --- L3: RestoreRequest layout (design §C2) ---

    <Fact>
    Public Sub L3_1_LegacyRequestShapeKeepsTheProjectAtTheCacheRoot()
        Dim cacheDirectory = CreateIsolatedTempDirectory()

        Dim request As New RestoreRequest(cacheDirectory, "<Project/>", "{}")

        Assert.Equal(Path.Combine(cacheDirectory, "project.vbproj"), request.ProjectFilePath)
        Assert.Equal(cacheDirectory, request.ProjectDirectory)
        Assert.True(request.MirrorFiles.IsDefaultOrEmpty)
    End Sub

    <Fact>
    Public Sub L3_2_MirrorSetAndRelativePathExtendTheRequest()
        Dim cacheDirectory = CreateIsolatedTempDirectory()
        Dim files = ImmutableArray.Create(New SourceConfigFile("NuGet.config", Encoding.UTF8.GetBytes("c")))
        Dim relativePath = Path.Combine("sub", "app", "project.vbproj")

        Dim request As New RestoreRequest(cacheDirectory, "<Project/>", "{}", files, relativePath)

        Assert.Equal(Path.Combine(cacheDirectory, "sub", "app", "project.vbproj"), request.ProjectFilePath)
        Assert.Equal(Path.Combine(cacheDirectory, "sub", "app"), request.ProjectDirectory)
        Assert.Equal(1, request.MirrorFiles.Length)
        Assert.Equal("NuGet.config", request.MirrorFiles(0).MirrorRelativePath)
    End Sub

    <Fact>
    Public Sub L3_3_RootedOrTraversingProjectPathIsRejected()
        Dim cacheDirectory = CreateIsolatedTempDirectory()

        Assert.Throws(Of ArgumentException)(Sub() CreateRequest(cacheDirectory, Path.Combine("..", "escape", "project.vbproj")))
        Assert.Throws(Of ArgumentException)(Sub() CreateRequest(cacheDirectory, Path.Combine(cacheDirectory, "project.vbproj")))
    End Sub

    Private Shared Function CreateRequest(cacheDirectory As String, projectRelativePath As String) As RestoreRequest
        Return New RestoreRequest(cacheDirectory, "<Project/>", "{}", ImmutableArray(Of SourceConfigFile).Empty, projectRelativePath)
    End Function

    ' --- L4: mirror write contract (design §C2, §E7 write-once) ---

    <Fact>
    Public Sub L4_8_MirrorFilesLandAtTheirRelativeDepthAndAreNeverRewritten()
        Dim cacheDirectory = CreateIsolatedTempDirectory()
        Dim files = ImmutableArray.Create(
            New SourceConfigFile("NuGet.config", Encoding.UTF8.GetBytes("root")),
            New SourceConfigFile(Path.Combine("sub", "NuGet.config"), Encoding.UTF8.GetBytes("sub")))

        Dim failurePath As String = "unset"
        Dim failureReason As String = "unset"
        Assert.True(DotNetRestoreRunner.WriteMirrorFiles(cacheDirectory, files, failurePath, failureReason))
        Assert.Null(failurePath)
        Assert.Null(failureReason)
        Assert.Equal("root", File.ReadAllText(Path.Combine(cacheDirectory, "NuGet.config")))
        Assert.Equal("sub", File.ReadAllText(Path.Combine(cacheDirectory, "sub", "NuGet.config")))

        ' Write-once: a file already in the key directory belongs to this exact key (the fingerprint is part
        ' of the key), so it is never rewritten — the byte-stability premise restore's no-op depends on.
        File.WriteAllText(Path.Combine(cacheDirectory, "NuGet.config"), "changed")
        Assert.True(DotNetRestoreRunner.WriteMirrorFiles(cacheDirectory, files, failurePath, failureReason))
        Assert.Equal("changed", File.ReadAllText(Path.Combine(cacheDirectory, "NuGet.config")))
    End Sub

    <Fact>
    Public Sub L4_9_MirrorWriteFailureIsReportedNotThrown()
        Dim cacheDirectory = CreateIsolatedTempDirectory()
        ' A file where a directory has to go makes Directory.CreateDirectory fail deterministically.
        File.WriteAllText(Path.Combine(cacheDirectory, "sub"), "blocker")
        Dim files = ImmutableArray.Create(New SourceConfigFile(Path.Combine("sub", "NuGet.config"), Encoding.UTF8.GetBytes("sub")))

        Dim failurePath As String = Nothing
        Dim failureReason As String = Nothing
        Assert.False(DotNetRestoreRunner.WriteMirrorFiles(cacheDirectory, files, failurePath, failureReason))
        Assert.Equal(Path.Combine(cacheDirectory, "sub", "NuGet.config"), failurePath)
        Assert.False(String.IsNullOrEmpty(failureReason))
    End Sub

    <Fact>
    Public Sub L4_10_ProjectAndGlobalJsonAreDeployedAtTheMirrorDepth()
        Dim cacheDirectory = CreateIsolatedTempDirectory()
        Dim files = ImmutableArray.Create(New SourceConfigFile("NuGet.config", Encoding.UTF8.GetBytes("root")))
        Dim request As New RestoreRequest(cacheDirectory, "<Project Sdk=""Microsoft.NET.Sdk"" />", "{}", files, Path.Combine("app", "project.vbproj"))

        ' Regression for a defect a real vbi run exposed: with the project below the mirror root its parent
        ' directory does not exist yet, and WriteOnce alone cannot create it — the DirectoryNotFoundException
        ' was swallowed into a bogus "SDK missing" diagnostic.
        Assert.False(System.IO.Directory.Exists(Path.Combine(cacheDirectory, "app")))
        DotNetRestoreRunner.WriteProjectFiles(request)

        Assert.Equal("<Project Sdk=""Microsoft.NET.Sdk"" />", File.ReadAllText(Path.Combine(cacheDirectory, "app", "project.vbproj")))
        Assert.Equal("{}", File.ReadAllText(Path.Combine(cacheDirectory, "global.json")))
    End Sub

    ' --- L4: coordinator wiring (design §C3/§C4/§C5) ---

    Private NotInheritable Class FakeRestoreRunner
        Implements IRestoreRunner

        Public Property SdkVersions As String() = New String() {"10.0.100"}
        Public Property Outcome As RestoreOutcome
        Public RestoreCalls As Integer
        Public LastRequest As RestoreRequest

        Public Function GetInstalledSdkVersionsAsync(cancellationToken As CancellationToken) As Task(Of ImmutableArray(Of String)) Implements IRestoreRunner.GetInstalledSdkVersionsAsync
            Return Task.FromResult(SdkVersions.ToImmutableArray())
        End Function

        Public Function RestoreAsync(request As RestoreRequest, cancellationToken As CancellationToken) As Task(Of RestoreOutcome) Implements IRestoreRunner.RestoreAsync
            RestoreCalls += 1
            LastRequest = request
            Return Task.FromResult(Outcome)
        End Function
    End Class

    Private Shared Function SucceededOutcome() As RestoreOutcome
        Return New RestoreOutcome(exitCode:=0, standardError:="", nuGetCacheWritten:=True, assetsJsonText:=ManagedAssetsJson)
    End Function

    Private Shared Function CreateCoordinator(session As NuGetPackageSession,
                                              runner As FakeRestoreRunner,
                                              fileSystem As ISourceConfigFileSystem,
                                              Optional sourceFingerprint As String = Nothing) As NuGetRestoreCoordinator
        Return New NuGetRestoreCoordinator(
            session,
            runner:=runner,
            cacheRoot:=CreateIsolatedTempDirectory(),
            sourceFingerprint:=sourceFingerprint,
            sourceFileSystem:=fileSystem)
    End Function

    Private Shared Function RuntimeIdentifierForTest() As String
        Return RuntimeInformation.RuntimeIdentifier
    End Function

    Private Shared Function ExpectedKey(session As NuGetPackageSession, fingerprint As String) As String
        Return NuGetRestoreCache.ComputeKey(
            ImmutableArray.Create(NuGetPackageSet.CanonicalKey("Contoso.Lib", "2.0.0")),
            session.HostCapability.FrameworkNameForRestore,
            RuntimeIdentifierForTest(),
            "10.0.100",
            fingerprint,
            session.HostCapability.IsNet48)
    End Function

    <Fact>
    Public Sub L4_1_NoDirectoryConfigKeepsTheCacheKeyAndProjectPathUnchanged()
        Dim fileSystem As New MemoryFileSystem()
        Dim session As New NuGetPackageSession()
        Dim runner As New FakeRestoreRunner()
        runner.Outcome = SucceededOutcome()
        Dim coordinator = CreateCoordinator(session, runner, fileSystem)

        Dim diagnostics = coordinator.PrepareCompilationAsync(SourceText.From(NuGetReference()), ScriptPath(), CancellationToken.None).GetAwaiter().GetResult()

        Assert.True(diagnostics.IsEmpty)
        Assert.Equal(1, runner.RestoreCalls)
        Assert.True(runner.LastRequest.MirrorFiles.IsDefaultOrEmpty)
        Assert.Equal(Path.Combine(runner.LastRequest.CacheDirectory, "project.vbproj"), runner.LastRequest.ProjectFilePath)
        ' The auto-computed fingerprint of an empty chain is the empty string, so the key is exactly the key
        ' this package set produced before the fingerprint was wired up.
        Assert.Equal(ExpectedKey(session, ""), Path.GetFileName(runner.LastRequest.CacheDirectory))
    End Sub

    <Fact>
    Public Sub L4_2_ConfigOnTheScriptChainIsMirroredAndMovesTheProjectToTheScriptDepth()
        Dim fileSystem As New MemoryFileSystem()
        Dim root = SourceRoot()
        fileSystem.AddFile(Path.Combine(root, "NuGet.config"), "root")
        Dim session As New NuGetPackageSession()
        Dim runner As New FakeRestoreRunner()
        runner.Outcome = SucceededOutcome()
        Dim coordinator = CreateCoordinator(session, runner, fileSystem)

        Dim diagnostics = coordinator.PrepareCompilationAsync(SourceText.From(NuGetReference()), ScriptPath(), CancellationToken.None).GetAwaiter().GetResult()

        Assert.True(diagnostics.IsEmpty)
        Assert.Equal(1, runner.LastRequest.MirrorFiles.Length)
        Assert.Equal("NuGet.config", runner.LastRequest.MirrorFiles(0).MirrorRelativePath)
        Assert.Equal("root", Encoding.UTF8.GetString(runner.LastRequest.MirrorFiles(0).Content))
        ' The script is one level below the mirror root (the directory holding the config), so the project is
        ' placed at the same depth and NuGet walks the mirrored config on its way up.
        Assert.Equal(Path.Combine(runner.LastRequest.CacheDirectory, "app", "project.vbproj"), runner.LastRequest.ProjectFilePath)
    End Sub

    <Fact>
    Public Sub L4_3_ChangingTheConfigContentChangesTheKeySoNothingIsReused()
        Dim firstKey = CacheKeyForConfig("one")
        Dim secondKey = CacheKeyForConfig("two")

        Assert.NotEqual(firstKey, secondKey)

        ' A config that changes back to a previously used content is cached again under the original key, so
        ' the fingerprint is a function of the content and not of history.
        Assert.Equal(firstKey, CacheKeyForConfig("one"))
    End Sub

    Private Shared Function CacheKeyForConfig(configContent As String) As String
        Return Path.GetFileName(RestoreCacheDirectoryForConfig(configContent, Nothing))
    End Function

    Private Shared Function RestoreCacheDirectoryForConfig(configContent As String, sourceFingerprint As String) As String
        Dim fileSystem As New MemoryFileSystem()
        fileSystem.AddFile(Path.Combine(ScriptDirectory(), "NuGet.config"), configContent)
        Dim runner As New FakeRestoreRunner()
        runner.Outcome = SucceededOutcome()
        Dim coordinator = CreateCoordinator(New NuGetPackageSession(), runner, fileSystem, sourceFingerprint)

        Dim diagnostics = coordinator.PrepareCompilationAsync(SourceText.From(NuGetReference()), ScriptPath(), CancellationToken.None).GetAwaiter().GetResult()
        Assert.True(diagnostics.IsEmpty)
        Return runner.LastRequest.CacheDirectory
    End Function

    <Fact>
    Public Sub L4_4_PureReplSubmissionHasNoConfigChainAndNoMirror()
        Dim fileSystem As New MemoryFileSystem()
        Dim session As New NuGetPackageSession()
        Dim runner As New FakeRestoreRunner()
        runner.Outcome = SucceededOutcome()
        Dim coordinator = CreateCoordinator(session, runner, fileSystem)

        Dim diagnostics = coordinator.PrepareCompilationAsync(SourceText.From(NuGetReference()), "", CancellationToken.None).GetAwaiter().GetResult()

        Assert.True(diagnostics.IsEmpty)
        Assert.True(runner.LastRequest.MirrorFiles.IsDefaultOrEmpty)
        Assert.Equal(Path.Combine(runner.LastRequest.CacheDirectory, "project.vbproj"), runner.LastRequest.ProjectFilePath)
        Assert.Equal(ExpectedKey(session, ""), Path.GetFileName(runner.LastRequest.CacheDirectory))
    End Sub

    <Fact>
    Public Sub L4_5_ExplicitSourceFingerprintStillOverridesTheCollectedOne()
        Dim fileSystem As New MemoryFileSystem()
        fileSystem.AddFile(Path.Combine(ScriptDirectory(), "NuGet.config"), "content")
        Dim session As New NuGetPackageSession()
        Dim runner As New FakeRestoreRunner()
        runner.Outcome = SucceededOutcome()
        Dim coordinator = CreateCoordinator(session, runner, fileSystem, sourceFingerprint:="pinned")

        Dim diagnostics = coordinator.PrepareCompilationAsync(SourceText.From(NuGetReference()), ScriptPath(), CancellationToken.None).GetAwaiter().GetResult()

        Assert.True(diagnostics.IsEmpty)
        Assert.Equal(ExpectedKey(session, "pinned"), Path.GetFileName(runner.LastRequest.CacheDirectory))
    End Sub

    <Fact>
    Public Sub L4_6_UnreadableConfigReportsAConfigurationDiagnosticAndSkipsRestore()
        Dim fileSystem As New MemoryFileSystem()
        Dim configPath = Path.Combine(ScriptDirectory(), "NuGet.config")
        fileSystem.AddFile(configPath, "content")
        fileSystem.FailOnRead = configPath
        Dim runner As New FakeRestoreRunner()
        runner.Outcome = SucceededOutcome()
        Dim coordinator = CreateCoordinator(New NuGetPackageSession(), runner, fileSystem)

        Dim diagnostics = coordinator.PrepareCompilationAsync(SourceText.From(NuGetReference()), ScriptPath(), CancellationToken.None).GetAwaiter().GetResult()

        Assert.Equal(1, diagnostics.Length)
        Assert.Equal("VBI1012", diagnostics(0).Id)
        Assert.Contains(configPath, diagnostics(0).GetMessage())
        Assert.Contains("access denied", diagnostics(0).GetMessage())
        ' Anchored at the script itself and never reported as a restore or SDK failure.
        Assert.Equal(ScriptPath(), diagnostics(0).Location.GetLineSpan().Path)
        Assert.Equal(0, runner.RestoreCalls)
    End Sub

    <Fact>
    Public Sub L4_7_MirrorWriteFailureReportsTheConfigurationDiagnostic()
        Dim fileSystem As New MemoryFileSystem()
        fileSystem.AddFile(Path.Combine(ScriptDirectory(), "NuGet.config"), "content")
        Dim runner As New FakeRestoreRunner()
        runner.Outcome = New RestoreOutcome(
            NuGetRestoreDiagnostics.ConfigFailureExitCode,
            String.Empty,
            nuGetCacheWritten:=False,
            assetsJsonText:=Nothing,
            configFailurePath:="C:\cache\NuGet.config",
            configFailureReason:="access denied")
        Dim coordinator = CreateCoordinator(New NuGetPackageSession(), runner, fileSystem)

        Dim diagnostics = coordinator.PrepareCompilationAsync(SourceText.From(NuGetReference()), ScriptPath(), CancellationToken.None).GetAwaiter().GetResult()

        Assert.Equal(1, diagnostics.Length)
        Assert.Equal("VBI1012", diagnostics(0).Id)
        Assert.Contains("access denied", diagnostics(0).GetMessage())
        Assert.Equal(ScriptPath(), diagnostics(0).Location.GetLineSpan().Path)
    End Sub
End Class
