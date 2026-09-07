' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' No-side-effect unit tests for the missing current-RID native-asset detector (design §G2.4). The detector
' is a pure function over project.assets.json text: NuGet already performed RID fallback, so the detector
' only reports a referenced package whose native assets cover other RIDs but not the host RID, which would
' otherwise surface later as a DllNotFoundException. No restore process and no file access happen here.

Imports System.Collections.Immutable
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting
Imports Xunit

Public Class NuGetMissingNativeAssetsTests

    Private Shared Function Request(name As String, version As String) As NuGetPackageRequest
        Return New NuGetPackageRequest(name, version, Location.None)
    End Function

    Private Const AssetsWithNativeForWin7X86 As String = "{""packageFolders"":{""C:/nuget/packages/"":{}},""libraries"":{""Contoso.Native/1.0.0"":{""type"":""package"",""path"":""contoso.native/1.0.0""}},""targets"":{""net10.0"":{""Contoso.Native/1.0.0"":{""type"":""package"",""compile"":{""lib/net10.0/Contoso.Native.dll"":{}},""runtime"":{""lib/net10.0/Contoso.Native.dll"":{}},""runtimeTargets"":{""runtimes/win7-x86/native/e_sqlite3.dll"":{""rid"":""win7-x86"",""assetType"":""native""}}}}}}"

    Private Const AssetsWithNativeForWinX64 As String = "{""packageFolders"":{""C:/nuget/packages/"":{}},""libraries"":{""Contoso.Native/1.0.0"":{""type"":""package"",""path"":""contoso.native/1.0.0""}},""targets"":{""net10.0"":{""Contoso.Native/1.0.0"":{""type"":""package"",""compile"":{""lib/net10.0/Contoso.Native.dll"":{}},""runtime"":{""lib/net10.0/Contoso.Native.dll"":{}},""runtimeTargets"":{""runtimes/win-x64/native/e_sqlite3.dll"":{""rid"":""win-x64"",""assetType"":""native""}}}}}}"

    Private Const AssetsManagedOnly As String = "{""packageFolders"":{""C:/nuget/packages/"":{}},""libraries"":{""Contoso.Lib/2.0.0"":{""type"":""package"",""path"":""contoso.lib/2.0.0""}},""targets"":{""net10.0"":{""Contoso.Lib/2.0.0"":{""type"":""package"",""compile"":{""lib/net10.0/Contoso.Lib.dll"":{}},""runtime"":{""lib/net10.0/Contoso.Lib.dll"":{}}}}}}"

    <Fact>
    Public Sub DetectorReportsPackageWhoseNativeAssetsSkipCurrentRid()
        Dim requests = ImmutableArray.Create(Request("Contoso.Native", "1.0.0"))
        Dim missing = NuGetMissingNativeAssetsDetector.FindMissingNativeForRid(AssetsWithNativeForWin7X86, "win-x64", requests)

        Assert.Equal(1, missing.Length)
        Assert.Equal("Contoso.Native", missing(0).PackageName)
        Assert.Contains("win7-x86", missing(0).CandidateRids)
    End Sub

    <Fact>
    Public Sub DetectorSilentWhenNativeAssetsCoverCurrentRid()
        Dim requests = ImmutableArray.Create(Request("Contoso.Native", "1.0.0"))
        Dim missing = NuGetMissingNativeAssetsDetector.FindMissingNativeForRid(AssetsWithNativeForWinX64, "win-x64", requests)

        Assert.True(missing.IsEmpty)
    End Sub

    <Fact>
    Public Sub DetectorSilentForManagedOnlyGraph()
        Dim requests = ImmutableArray.Create(Request("Contoso.Lib", "2.0.0"))
        Dim missing = NuGetMissingNativeAssetsDetector.FindMissingNativeForRid(AssetsManagedOnly, "win-x64", requests)

        Assert.True(missing.IsEmpty)
    End Sub

    <Fact>
    Public Sub DetectorSilentForEmptyRid()
        Dim requests = ImmutableArray.Create(Request("Contoso.Native", "1.0.0"))
        Dim missing = NuGetMissingNativeAssetsDetector.FindMissingNativeForRid(AssetsWithNativeForWin7X86, "", requests)

        Assert.True(missing.IsEmpty)
    End Sub

    <Fact>
    Public Sub DetectorIgnoresUnrequestedPackage()
        ' A request for a managed package next to an unrelated native package must not be reported.
        Dim requests = ImmutableArray.Create(Request("Contoso.Lib", "2.0.0"))
        Dim missing = NuGetMissingNativeAssetsDetector.FindMissingNativeForRid(AssetsWithNativeForWin7X86, "win-x64", requests)

        Assert.True(missing.IsEmpty)
    End Sub
End Class
