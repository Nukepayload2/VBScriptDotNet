' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' No-side-effect L1 tests for the host-level command line normalization
' (VbiCommandLine.NormalizeLongSwitches). They only feed argument arrays in and assert the returned
' array; they touch no files, processes, network or console. The end-to-end pairing with the real
' runner lives in CommandLineRunnerTests.Version, which asserts "--version" prints the version output.

Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting
Imports Xunit

Public Class VbiCommandLineTests

    <Fact>
    Public Sub DoubleDashSwitchIsFolded()
        AssertNormalized({"/version"}, "--version")
        AssertNormalized({"/help"}, "--help")
        AssertNormalized({"/check"}, "--check")
    End Sub

    <Fact>
    Public Sub SwitchWithValueIsFolded()
        AssertNormalized({"/out:app.dll"}, "--out:app.dll")
        AssertNormalized({"/reference:lib.dll"}, "--reference:lib.dll")
        AssertNormalized({"/imports:System.Text"}, "--imports:System.Text")
    End Sub

    <Fact>
    Public Sub EveryArgumentBeforeTheSeparatorIsFolded()
        AssertNormalized({"/help", "/check", "script.vbx", "/i"}, "--help", "--check", "script.vbx", "--i")
    End Sub

    <Fact>
    Public Sub SingleDashAndSlashFormsAreUnchanged()
        AssertNormalized({"/version", "-version", "/?", "-?", "/i", "-i"}, "/version", "-version", "/?", "-?", "/i", "-i")
    End Sub

    <Fact>
    Public Sub BareSeparatorEndsTheFold()
        AssertNormalized({"/version", "script.vbx", "--", "--help", "--out:app.dll"},
                         "--version", "script.vbx", "--", "--help", "--out:app.dll")
    End Sub

    <Fact>
    Public Sub NonSwitchesAreUnchanged()
        AssertNormalized({"script.vbx", "-", "/"}, "script.vbx", "-", "/")
    End Sub

    <Fact>
    Public Sub NothingAndEmptyAreSafe()
        Assert.Empty(VbiCommandLine.NormalizeLongSwitches(Nothing))
        Assert.Empty(VbiCommandLine.NormalizeLongSwitches(Array.Empty(Of String)()))
    End Sub

    <Fact>
    Public Sub InputArrayIsNotMutated()
        Dim args = {"--version"}
        VbiCommandLine.NormalizeLongSwitches(args)
        Assert.Equal("--version", args(0))
    End Sub

    Private Shared Sub AssertNormalized(expected As String(), ParamArray args As String())
        Assert.Equal(expected, VbiCommandLine.NormalizeLongSwitches(args))
    End Sub

End Class
