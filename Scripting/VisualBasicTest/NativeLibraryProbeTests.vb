' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' No-side-effect unit tests for the cross-platform native probe candidate table (design §G2.3, the pure
' part of the G2-3 gated checklist). The loader mechanism lives in Scripting Core
' (CoreAssemblyLoaderImpl.LoadUnmanagedDll); this locks the candidate naming/search order a host RID root
' is probed with: verbatim name, name + platform extension, then the Unix "lib"+name+extension. Nothing is
' loaded or touched on disk.

Imports System.Collections.Immutable
Imports System.IO
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Xunit

Public Class NativeLibraryProbeTests

    Private Shared Function Roots(ParamArray directories As String()) As ImmutableArray(Of String)
        Return directories.ToImmutableArray()
    End Function

    <Fact>
    Public Sub ProbeCandidatesOrderVerbatimExtensionThenLibPrefixed()
        Dim root = "C:/pkgs/contoso.native/1.0.0/runtimes/win-x64/native"
        Dim candidates = NativeLibraryProbe.GetProbeCandidates(Roots(root), "e_sqlite3", ".dll")

        Assert.Equal(3, candidates.Length)
        Assert.Equal(Path.Combine(root, "e_sqlite3"), candidates(0))
        Assert.Equal(Path.Combine(root, "e_sqlite3.dll"), candidates(1))
        Assert.Equal(Path.Combine(root, "libe_sqlite3.dll"), candidates(2))
    End Sub

    <Fact>
    Public Sub ProbeCandidatesLinuxUsesDotSoAndLibPrefix()
        Dim root = "/pkgs/contoso.native/1.0.0/runtimes/linux-x64/native"
        Dim candidates = NativeLibraryProbe.GetProbeCandidates(Roots(root), "e_sqlite3", ".so")

        Assert.Equal(3, candidates.Length)
        Assert.Equal(Path.Combine(root, "e_sqlite3"), candidates(0))
        Assert.Equal(Path.Combine(root, "e_sqlite3.so"), candidates(1))
        Assert.Equal(Path.Combine(root, "libe_sqlite3.so"), candidates(2))
    End Sub

    <Fact>
    Public Sub ProbeCandidatesMacUsesDotDylibAndLibPrefix()
        Dim root = "/pkgs/contoso.native/1.0.0/runtimes/osx-x64/native"
        Dim candidates = NativeLibraryProbe.GetProbeCandidates(Roots(root), "e_sqlite3", ".dylib")

        Assert.Equal(3, candidates.Length)
        Assert.Equal(Path.Combine(root, "e_sqlite3"), candidates(0))
        Assert.Equal(Path.Combine(root, "e_sqlite3.dylib"), candidates(1))
        Assert.Equal(Path.Combine(root, "libe_sqlite3.dylib"), candidates(2))
    End Sub

    <Fact>
    Public Sub ProbeCandidatesIterateEveryRootInOrderBeforeNextName()
        Dim firstRoot = "/r1/native"
        Dim secondRoot = "/r2/native"
        Dim candidates = NativeLibraryProbe.GetProbeCandidates(Roots(firstRoot, secondRoot), "sqlite3", ".dll")

        Assert.Equal(6, candidates.Length)
        Assert.Equal(Path.Combine(firstRoot, "sqlite3"), candidates(0))
        Assert.Equal(Path.Combine(firstRoot, "sqlite3.dll"), candidates(1))
        Assert.Equal(Path.Combine(firstRoot, "libsqlite3.dll"), candidates(2))
        Assert.Equal(Path.Combine(secondRoot, "sqlite3"), candidates(3))
        Assert.Equal(Path.Combine(secondRoot, "sqlite3.dll"), candidates(4))
        Assert.Equal(Path.Combine(secondRoot, "libsqlite3.dll"), candidates(5))
    End Sub

    <Fact>
    Public Sub ProbeCandidatesEmptyRootsReturnEmpty()
        Assert.True(NativeLibraryProbe.GetProbeCandidates(ImmutableArray(Of String).Empty, "e_sqlite3", ".dll").IsEmpty)
    End Sub

    <Fact>
    Public Sub ProbeCandidatesEmptyNameReturnsEmpty()
        Dim nativeRoot = Roots("C:/native")
        Assert.True(NativeLibraryProbe.GetProbeCandidates(nativeRoot, "", ".dll").IsEmpty)
        Assert.True(NativeLibraryProbe.GetProbeCandidates(nativeRoot, Nothing, ".dll").IsEmpty)
    End Sub

    <Fact>
    Public Sub ProbeCandidatesNameWithDirectorySeparatorIsLeftToDefaultResolution()
        Dim nativeRoot = Roots("C:/native")
        Assert.True(NativeLibraryProbe.GetProbeCandidates(nativeRoot, "sub/e_sqlite3", ".dll").IsEmpty)
        Assert.True(NativeLibraryProbe.GetProbeCandidates(nativeRoot, "sub\e_sqlite3", ".dll").IsEmpty)
    End Sub

    <Fact>
    Public Sub PlatformExtensionMatchesRunningOsShape()
        Dim extension = NativeLibraryProbe.GetPlatformNativeExtension()
        Assert.Contains(extension, New String() {".dll", ".so", ".dylib"})
        Assert.StartsWith(".", extension)
    End Sub
End Class
