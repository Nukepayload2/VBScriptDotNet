' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' No-side-effect unit tests for the V-E restore engine pure functions (design §E3/§E4): ShouldRestore trigger
' policy, ExitCodeToDiagnostic translation table, NuGetSdkResolver selection, project.assets.json reading and
' the byte-stable temporary-project generator. None of these spawn a process or touch disk.

Imports System.Collections.Immutable
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting
Imports Xunit

Public Class NuGetRestoreEngineTests

    Private Const Quote As String = """"

    ' --- ShouldRestore (design §E3 trigger table) ---

    Private Shared Function Keys(ParamArray values As String()) As ImmutableArray(Of String)
        Return NuGetPackageSet.Normalize(values)
    End Function

    <Fact>
    Public Sub ShouldRestoreSameSetSkips()
        Dim previous = Keys("a,1.0.0", "b,2.0.0")
        Dim current = Keys("b,2.0.0", "a,1.0.0")
        Assert.False(NuGetRestorePolicy.ShouldRestore(previous, current))
    End Sub

    <Fact>
    Public Sub ShouldRestoreSetChangeRestores()
        Dim previous = Keys("a,1.0.0")
        Dim current = Keys("a,1.0.0", "b,2.0.0")
        Assert.True(NuGetRestorePolicy.ShouldRestore(previous, current))
    End Sub

    <Fact>
    Public Sub ShouldRestoreFreshProcessAlwaysRestores()
        ' Empty previous set = a fresh host (or the previous restore failed); the same key must be restored
        ' again across processes (NuGet no-op restore is the cheap re-verification).
        Dim current = Keys("a,1.0.0")
        Assert.True(NuGetRestorePolicy.ShouldRestore(ImmutableArray(Of String).Empty, current))
    End Sub

    <Fact>
    Public Sub ShouldRestoreEmptyCurrentNeverRestores()
        Assert.False(NuGetRestorePolicy.ShouldRestore(ImmutableArray(Of String).Empty, ImmutableArray(Of String).Empty))
        Assert.False(NuGetRestorePolicy.ShouldRestore(Keys("a,1.0.0"), ImmutableArray(Of String).Empty))
    End Sub

    <Fact>
    Public Sub PackageSetNormalizeSortsAndDeduplicatesCaseInsensitively()
        Dim normalized = Keys("B,1.0.0", "a,1.0.0", "A,1.0.0")
        Assert.Equal(2, normalized.Length)
        Assert.Equal("a,1.0.0", normalized(0))
        ' The first seen casing is kept ("B" from the input); identity itself is case-insensitive.
        Assert.Equal("B,1.0.0", normalized(1))
    End Sub

    ' --- ExitCodeToDiagnostic (design §E3 translation table) ---

    <Fact>
    Public Sub ExitCodeToDiagnosticSdkMissingOnDotNetStartFailure()
        Dim diag = NuGetRestoreDiagnostics.ExitCodeToDiagnostic(
            NuGetRestoreDiagnostics.DotNetMissingExitCode, "", Location.None, "Contoso.Widget", "1.0.0")
        Assert.Equal("VBI1006", diag.Id)
        Assert.Contains(".NET SDK", diag.GetMessage())
    End Sub

    <Fact>
    Public Sub ExitCodeToDiagnosticNu1101ReportsPackageNotFound()
        Dim stderr = "error NU1101: Unable to find package Contoso.Widget. No packages exist with this id in source(s): nuget.org"
        Dim diag = NuGetRestoreDiagnostics.ExitCodeToDiagnostic(1, stderr, Location.None, "Contoso.Widget", "1.0.0")
        Assert.Equal("VBI1007", diag.Id)
        Assert.Contains("Contoso.Widget", diag.GetMessage())
    End Sub

    <Fact>
    Public Sub ExitCodeToDiagnosticNetworkFailureReportsUnableToConnect()
        Dim stderr = "error NU1301: Unable to load the service index for source https://api.nuget.org/v3/index.json."
        Dim diag = NuGetRestoreDiagnostics.ExitCodeToDiagnostic(1, stderr, Location.None, "Contoso.Widget", "1.0.0")
        Assert.Equal("VBI1008", diag.Id)
    End Sub

    <Fact>
    Public Sub ExitCodeToDiagnosticOtherFailureSummarizesFirstLineWithExitCode()
        Dim stderr = "error : Failed to restore because of an unknown reason" & vbCrLf & "tail not included"
        Dim diag = NuGetRestoreDiagnostics.ExitCodeToDiagnostic(5, stderr, Location.None, "Contoso.Widget", "1.0.0")
        Assert.Equal("VBI1009", diag.Id)
        Dim message = diag.GetMessage()
        Assert.Contains("exit code 5", message)
        Assert.Contains("Failed to restore", message)
        Assert.DoesNotContain("tail not included", message)
    End Sub

    ' --- NuGetSdkResolver (design §E3 SDK gate) ---

    <Fact>
    Public Sub SdkResolverSelectsHighestInstalledVersion()
        Dim selected = NuGetSdkResolver.SelectSdkVersion(ImmutableArray.Create("9.0.200", "10.0.100"))
        Assert.Equal("10.0.100", selected)
    End Sub

    <Fact>
    Public Sub SdkResolverReturnsNothingWhenNoSdkInstalled()
        Assert.Null(NuGetSdkResolver.SelectSdkVersion(ImmutableArray(Of String).Empty))
    End Sub

    ' --- ReadAssets (design §E4) ---

    Private Const AssetsJson As String = "{""packageFolders"":[""C:/nuget/packages/""],""libraries"":{""Contoso.Main/2.0.0"":{""type"":""package"",""path"":""contoso.main/2.0.0""},""Newtonsoft.Json/13.0.3"":{""type"":""package"",""path"":""newtonsoft.json/13.0.3""},""Contoso.Native/1.0.0"":{""type"":""package"",""path"":""contoso.native/1.0.0""}},""targets"":{ "".NETCoreApp,Version=v10.0"":{""Contoso.Main/2.0.0"":{""type"":""package"",""compile"":{""ref/net10.0/Contoso.Main.dll"":{},""lib/net10.0/Contoso.Main.dll"":{}},""runtime"":{""lib/net10.0/Contoso.Main.dll"":{}},""dependencies"":{""Newtonsoft.Json/13.0.3"":{}}},""Newtonsoft.Json/13.0.3"":{""type"":""package"",""compile"":{""lib/netstandard2.0/Newtonsoft.Json.dll"":{}},""runtime"":{""lib/netstandard2.0/Newtonsoft.Json.dll"":{}}},""Contoso.Native/1.0.0"":{""type"":""package"",""compile"":{""lib/net10.0/Contoso.Native.dll"":{}},""runtime"":{""lib/net10.0/Contoso.Native.dll"":{}},""runtimeTargets"":{""runtimes/win-x64/native/e_sqlite3.dll"":{""rid"":""win-x64"",""assetType"":""native""}}}}}}"

    Private Shared Function Request(name As String, version As String) As NuGetPackageRequest
        Return New NuGetPackageRequest(name, version, Location.None)
    End Function

    <Fact>
    Public Sub ReadAssetsParsesCompileRuntimeAndNativeRoots()
        Dim requests = ImmutableArray.Create(Request("Contoso.Main", "2.0.0"), Request("Contoso.Native", "1.0.0"))
        Dim assets = NuGetRestoreAssetsReader.ReadAssets(AssetsJson, ".NETCoreApp,Version=v10.0", "", requests)

        ' Main package lists its own ref/ compile asset first, then its dependency closure.
        Dim mainCompile = assets.CompilePathsByCanonicalKey("contoso.main,2.0.0")
        Assert.False(mainCompile.IsDefaultOrEmpty)
        Assert.Equal(2, mainCompile.Length)
        Assert.EndsWith("/ref/net10.0/Contoso.Main.dll", mainCompile(0), StringComparison.OrdinalIgnoreCase)
        Assert.EndsWith("/newtonsoft.json/13.0.3/lib/netstandard2.0/Newtonsoft.Json.dll", mainCompile(1), StringComparison.OrdinalIgnoreCase)

        ' A package with no ref/ keeps its lib/ compile asset.
        Dim nativeCompile = assets.CompilePathsByCanonicalKey("contoso.native,1.0.0")
        Assert.Equal(1, nativeCompile.Length)
        Assert.EndsWith("/lib/net10.0/Contoso.Native.dll", nativeCompile(0), StringComparison.OrdinalIgnoreCase)

        ' Runtime (implementation) assets include every restored package.
        Assert.Contains(assets.RuntimePaths, Function(p) p.EndsWith("/lib/net10.0/Contoso.Main.dll", StringComparison.OrdinalIgnoreCase))
        Assert.Contains(assets.RuntimePaths, Function(p) p.EndsWith("/newtonsoft.json/13.0.3/lib/netstandard2.0/Newtonsoft.Json.dll", StringComparison.OrdinalIgnoreCase))

        ' Native probe root is the runtimes/<rid>/native folder of the package that owns the native dll.
        Assert.True(assets.HasNativeAssets)
        Assert.Equal(1, assets.NativeRootDirectories.Length)
        Assert.Equal("C:/nuget/packages/contoso.native/1.0.0/runtimes/win-x64/native", assets.NativeRootDirectories(0))
    End Sub

    ' --- Temporary project generator (design §E byte-stable) ---

    <Fact>
    Public Sub ProjectGeneratorNet48IncludesReferenceAssemblies()
        Dim project = NuGetProjectGenerator.GenerateProjectXml("net48", net48:=True, packages:=ImmutableArray(Of NuGetPackageRequest).Empty)
        Assert.Contains("Microsoft.NETFramework.ReferenceAssemblies.net48", project)
        Assert.Contains("<TargetFramework>net48</TargetFramework>", project)
    End Sub

    <Fact>
    Public Sub ProjectGeneratorNet10OmitsReferenceAssemblies()
        Dim project = NuGetProjectGenerator.GenerateProjectXml("net10.0", net48:=False, packages:=ImmutableArray(Of NuGetPackageRequest).Empty)
        Assert.DoesNotContain("Microsoft.NETFramework.ReferenceAssemblies", project)
    End Sub

    <Fact>
    Public Sub ProjectGeneratorIsByteStable()
        Dim packages = ImmutableArray.Create(Request("Contoso.Widget", "1.0.0"), Request("Alpha.Pkg", "2.0.0"))
        Dim first = NuGetProjectGenerator.GenerateProjectXml("net10.0", net48:=False, packages:=packages, runtimeIdentifier:="win-x64")
        Dim second = NuGetProjectGenerator.GenerateProjectXml("net10.0", net48:=False, packages:=packages, runtimeIdentifier:="win-x64")
        Assert.Equal(first, second)
        Assert.Contains("Alpha.Pkg", first)
        Assert.Contains("Contoso.Widget", first)
    End Sub

    <Fact>
    Public Sub GlobalJsonPinsSdkWithLatestMajorRollForward()
        Dim json = NuGetProjectGenerator.GenerateGlobalJson("10.0.100")
        Assert.Equal("{""sdk"":{""version"":""10.0.100"",""rollForward"":""latestMajor""}}", json)
    End Sub

    ' --- Cache key (design §E1/§E2) ---

    <Fact>
    Public Sub CacheKeyIsDeterministicAndDistinguishesPackageSets()
        Dim setA = Keys("a,1.0.0", "b,2.0.0")
        Dim setB = Keys("a,1.0.0")

        Dim keyA1 = NuGetRestoreCache.ComputeKey(setA, ".NETCoreApp,Version=v10.0", "win-x64", "10.0.100", "", net48:=False)
        Dim keyA2 = NuGetRestoreCache.ComputeKey(setA, ".NETCoreApp,Version=v10.0", "win-x64", "10.0.100", "", net48:=False)
        Dim keyB = NuGetRestoreCache.ComputeKey(setB, ".NETCoreApp,Version=v10.0", "win-x64", "10.0.100", "", net48:=False)

        Assert.Equal(keyA1, keyA2)
        Assert.NotEqual(keyA1, keyB)
        Assert.Equal(64, keyA1.Length)
    End Sub
End Class
