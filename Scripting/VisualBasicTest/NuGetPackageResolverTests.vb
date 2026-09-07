' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' 本 fork 有意偏离上游：以下测试锁定本 fork 对 NuGetPackageResolver.TryParsePackageReference 的语法解析
' （OrdinalIgnoreCase 前缀 + 逗号拆分 + 段 Trim）。上游无对应 NuGetPackageResolverTests；若上游将解析改回
' 斜杠分隔或仅接受小写前缀，本测试将失败以阻止误回滚。

Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Xunit

Public Class NuGetPackageResolverTests

    <Fact>
    Public Sub CommaSeparatesNameAndVersion()
        Dim name As String = Nothing
        Dim version As String = Nothing

        Dim result = NuGetPackageResolver.TryParsePackageReference("nuget:Newtonsoft.Json, 13.0.3", name, version)

        Assert.True(result)
        Assert.Equal("Newtonsoft.Json", name)
        Assert.Equal("13.0.3", version)
    End Sub

    <Fact>
    Public Sub PrefixIsCaseInsensitive()
        For Each prefix As String In New String() {"NuGet:", "NUGET:"}
            Dim name As String = Nothing
            Dim version As String = Nothing

            Dim result = NuGetPackageResolver.TryParsePackageReference(prefix & "Newtonsoft.Json, 13.0.3", name, version)

            Assert.True(result)
            Assert.Equal("Newtonsoft.Json", name)
            Assert.Equal("13.0.3", version)
        Next
    End Sub

    <Fact>
    Public Sub SegmentsAreTrimmed()
        Dim name As String = Nothing
        Dim version As String = Nothing

        Dim result = NuGetPackageResolver.TryParsePackageReference("nuget: Newtonsoft.Json , 13.0.3 ", name, version)

        Assert.True(result)
        Assert.Equal("Newtonsoft.Json", name)
        Assert.Equal("13.0.3", version)
    End Sub

    <Fact>
    Public Sub NearMissesAreRejected()
        Dim references = New String() {
            "nugt:X, 1.0",
            "nuget ：X",
            "nuget:",
            "nuget:, 1.0",
            "nuget: , 1.0"
        }

        For Each reference As String In references
            Dim name As String = Nothing
            Dim version As String = Nothing

            Assert.False(NuGetPackageResolver.TryParsePackageReference(reference, name, version))
            Assert.Null(name)
            Assert.Null(version)
        Next
    End Sub

    <Fact>
    Public Sub VersionCanBeOmitted()
        Dim name As String = Nothing
        Dim version As String = Nothing

        Dim result = NuGetPackageResolver.TryParsePackageReference("nuget:Newtonsoft.Json", name, version)

        Assert.True(result)
        Assert.Equal("Newtonsoft.Json", name)
        Assert.Equal(String.Empty, version)
    End Sub

End Class
