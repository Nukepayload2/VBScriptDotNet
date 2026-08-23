' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' No-side-effect L1 tests for the vbi compile-mode mode detection (B2 / F9). These only feed argument
' arrays into VbiCompileMode.IsCompileInvocation and assert the returned Boolean; they touch no files,
' processes, network or console.

Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting
Imports Xunit

Public Class VbiCompileModeTests

    <Fact>
    Public Sub VbSourceSelectsCompile()
        AssertCompile(True, "source.vb")
    End Sub

    <Fact>
    Public Sub VbSourceExtensionIsCaseInsensitive()
        AssertCompile(True, "SOURCE.VB")
    End Sub

    <Fact>
    Public Sub OutSwitchSelectsCompile()
        AssertCompile(True, "/out:app.dll")
    End Sub

    <Fact>
    Public Sub OutSwitchIsCaseInsensitive()
        AssertCompile(True, "/OUT:app.dll")
    End Sub

    <Fact>
    Public Sub TargetSwitchSelectsCompile()
        AssertCompile(True, "/target:library")
    End Sub

    <Fact>
    Public Sub TargetSwitchIsCaseInsensitive()
        AssertCompile(True, "/Target:library")
    End Sub

    <Fact>
    Public Sub VbxWithoutOutSelectsExecute()
        AssertCompile(False, "script.vbx")
    End Sub

    <Fact>
    Public Sub VbxWithOutSelectsCompile()
        AssertCompile(True, "script.vbx", "/out:app.dll")
    End Sub

    <Fact>
    Public Sub NoArgumentsSelectsRepl()
        AssertCompile(False)
    End Sub

    <Fact>
    Public Sub IForcesInteractiveEvenWithVbSource()
        AssertCompile(False, "source.vb", "/i")
    End Sub

    <Fact>
    Public Sub IPlusForcesInteractive()
        AssertCompile(False, "source.vb", "/i+")
    End Sub

    <Fact>
    Public Sub HelpMaintainsInteractivePath()
        AssertCompile(False, "/?")
    End Sub

    <Fact>
    Public Sub VersionSwitchMaintainsInteractivePath()
        AssertCompile(False, "/version")
    End Sub

    <Fact>
    Public Sub DoubleDashVersionMaintainsInteractivePath()
        AssertCompile(False, "--version")
    End Sub

    <Fact>
    Public Sub DoubleDashTreatsFollowingArgumentsAsScriptArguments()
        ' .vbx execution followed by -- script arguments, including a .vb file, stays in execute mode.
        AssertCompile(False, "script.vbx", "--", "foo.vb", "/out:app.dll")
    End Sub

    <Fact>
    Public Sub DoubleDashDoesNotHideEarlierCompileTrigger()
        AssertCompile(True, "source.vb", "/out:app.dll", "--", "arg")
    End Sub

    <Fact>
    Public Sub OutOptionWithoutColonIsNotMatched()
        AssertCompile(False, "/out", "app.dll")
    End Sub

    Private Shared Sub AssertCompile(expected As Boolean, ParamArray args As String())
        Dim actual = VbiCompileMode.IsCompileInvocation(args)
        Assert.Equal(expected, actual)
    End Sub

End Class
