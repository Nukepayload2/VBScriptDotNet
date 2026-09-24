' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.IO
Imports System.Runtime.InteropServices
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Test.Utilities
Imports Microsoft.CodeAnalysis.VisualBasic.UnitTests
Imports Roslyn.Test.Utilities
Imports Roslyn.Utilities
Imports Xunit

Namespace Microsoft.CodeAnalysis.VisualBasic.CommandLine.UnitTests

    ''' <summary>
    ''' The script command line parser ends option processing at a literal "--" (C# parity:
    ''' CSharpCommandLineParser.optionsEnded). The vbc parser must keep its current behavior.
    ''' </summary>
    Public Class ScriptOptionsEndedTests
        Inherits BasicTestBase

        Private ReadOnly _baseDirectory As String = TempRoot.Root

        Private Shared ReadOnly s_defaultSdkDirectory As String = If(
            RuntimeUtilities.IsCoreClrRuntime,
            Path.Combine(Path.GetDirectoryName(GetType(CommandLineTests).Assembly.Location), "dependency"),
            RuntimeEnvironment.GetRuntimeDirectory())

        Private Shared Function ScriptParse(args As IEnumerable(Of String), baseDirectory As String) As VisualBasicCommandLineArguments
            Return VisualBasicCommandLineParser.Script.Parse(args, baseDirectory, s_defaultSdkDirectory)
        End Function

        Private Shared Function VbcParse(args As IEnumerable(Of String), baseDirectory As String) As VisualBasicCommandLineArguments
            Return VisualBasicCommandLineParser.Default.Parse(args, baseDirectory, s_defaultSdkDirectory)
        End Function

        ''' <summary>
        ''' OE-A1: "@arg1" after "--" never reaches the option path, so the response-file assertion at
        ''' the top of the parse loop cannot be tripped; it becomes the source file instead.
        ''' </summary>
        <Fact>
        Public Sub DashDash_ResponseFileFormBecomesSourceFile()
            Dim args = ScriptParse({"--", "@arg1"}, _baseDirectory)

            args.Errors.Verify()
            Assert.Equal(1, args.SourceFiles.Length)
            AssertEx.Equal({Path.Combine(_baseDirectory, "@arg1")}, args.SourceFiles.Select(Function(file) file.Path))
            Assert.True(args.SourceFiles(0).IsScript)
            Assert.Equal(0, args.ScriptArguments.Length)
        End Sub

        ''' <summary>
        ''' OE-A2: "/arg2" after "--" is a positional argument, not BC2007; the source file slot is no
        ''' longer the "-" (stdin) slot that "--" used to fall into.
        ''' </summary>
        <Fact>
        Public Sub DashDash_NextSlashSwitchIsSourceFile()
            Dim args = ScriptParse({"--", "/arg2", "script.vbx"}, _baseDirectory)

            args.Errors.Verify()
            Assert.Equal(1, args.SourceFiles.Length)
            AssertEx.Equal({Path.GetPathRoot(_baseDirectory) + "arg2"}, args.SourceFiles.Select(Function(file) file.Path))
            Assert.DoesNotContain("-", args.SourceFiles.Select(Function(file) file.Path))
            Assert.True(args.SourceFiles(0).IsScript)
            AssertEx.Equal({"script.vbx"}, args.ScriptArguments)
        End Sub

        ''' <summary>
        ''' OE-A3: mirrors C# CommandLine_ScriptRunner1 ("--" -script.csx b c).
        ''' </summary>
        <Fact>
        Public Sub DashDash_NextDashSwitchIsSourceFile()
            Dim args = ScriptParse({"--", "-arg3", "b", "c"}, _baseDirectory)

            args.Errors.Verify()
            Assert.Equal(1, args.SourceFiles.Length)
            AssertEx.Equal({Path.Combine(_baseDirectory, "-arg3")}, args.SourceFiles.Select(Function(file) file.Path))
            AssertEx.Equal({"b", "c"}, args.ScriptArguments)
        End Sub

        ''' <summary>
        ''' OE-B1: options before "--" keep their current treatment, including BC2007 for a switch the
        ''' script parser does not know.
        ''' </summary>
        <Fact>
        Public Sub OptionsBeforeDashDashAreStillRejected()
            Dim args = ScriptParse({"/nosuchswitch", "--", "@arg4"}, _baseDirectory)

            args.Errors.Verify(Diagnostic(ERRID.WRN_BadSwitch).WithArguments("/nosuchswitch").WithLocation(1, 1))
            Assert.Equal(1, args.SourceFiles.Length)
            AssertEx.Equal({Path.Combine(_baseDirectory, "@arg4")}, args.SourceFiles.Select(Function(file) file.Path))
            Assert.Equal(0, args.ScriptArguments.Length)
        End Sub

        ''' <summary>
        ''' OE-B2: a single "-" keeps the stdin shape it has today.
        ''' </summary>
        <Fact>
        Public Sub SingleDashStillMeansStdIn()
            Dim args = ScriptParse({"-"}, _baseDirectory)

            If Console.IsInputRedirected Then
                args.Errors.Verify()
                AssertEx.Equal({"-"}, args.SourceFiles.Select(Function(file) file.Path))
                Assert.True(args.SourceFiles(0).IsScript)
            Else
                args.Errors.Verify(Diagnostic(ERRID.ERR_StdInOptionProvidedButConsoleInputIsNotRedirected).WithLocation(1, 1))
                Assert.Equal(0, args.SourceFiles.Length)
            End If
        End Sub

        ''' <summary>
        ''' OE-B3: only the exact separator "--" ends options. "--:x" is not the separator, so the
        ''' argument that follows is still expanded as a response file, and here still rejected.
        ''' </summary>
        <Fact>
        Public Sub OnlyTheExactSeparatorEndsOptions()
            Dim args = ScriptParse({"--:x", "@missing"}, _baseDirectory)

            Dim expectedErrors = New List(Of DiagnosticDescription) From {
                Diagnostic(ERRID.ERR_NoResponseFile).WithArguments(Path.Combine(_baseDirectory, "missing")).WithLocation(1, 1)}

            If Not Console.IsInputRedirected Then
                expectedErrors.Add(Diagnostic(ERRID.ERR_StdInOptionProvidedButConsoleInputIsNotRedirected).WithLocation(1, 1))
                Assert.Equal(0, args.SourceFiles.Length)
            Else
                AssertEx.Equal({"-"}, args.SourceFiles.Select(Function(file) file.Path))
            End If

            args.Errors.Verify(expectedErrors.ToArray())
        End Sub

        ''' <summary>
        ''' OE-C1 (non-script immunity): vbc keeps today's treatment of "--" (the stdin switch) and of
        ''' the "/arg2" that follows it (BC2007, never a source file).
        ''' </summary>
        <Fact>
        Public Sub VbcDashDashDoesNotEndOptionProcessing()
            Dim args = VbcParse({"--", "/arg2", "a.vb"}, _baseDirectory)

            Dim expectedPaths = New List(Of String)()
            Dim expectedErrors = New List(Of DiagnosticDescription) From {
                Diagnostic(ERRID.WRN_BadSwitch).WithArguments("/arg2").WithLocation(1, 1)}

            If Not Console.IsInputRedirected Then
                expectedErrors.Insert(0, Diagnostic(ERRID.ERR_StdInOptionProvidedButConsoleInputIsNotRedirected).WithLocation(1, 1))
            Else
                expectedPaths.Add("-")
            End If

            expectedPaths.Add(Path.Combine(_baseDirectory, "a.vb"))
            args.Errors.Verify(expectedErrors.ToArray())
            AssertEx.Equal(expectedPaths.ToArray(), args.SourceFiles.Select(Function(file) file.Path))
        End Sub

        ''' <summary>
        ''' OE-C2 (non-script immunity): for vbc an "@response file" after "--" is still expanded, i.e.
        ''' the gate never opens on the non-script path.
        ''' </summary>
        <Fact>
        Public Sub VbcResponseFileAfterDashDashIsStillExpanded()
            Dim rsp = Temp.CreateFile(extension:=".rsp").WriteAllText("/nosuchswitch" + Environment.NewLine + "b.vb")
            Dim args = VbcParse({"--", "@" + rsp.Path}, _baseDirectory)

            Dim expectedPaths = New List(Of String)()
            If Console.IsInputRedirected Then
                args.Errors.Verify(Diagnostic(ERRID.WRN_BadSwitch).WithArguments("/nosuchswitch").WithLocation(1, 1))
                expectedPaths.Add("-")
            Else
                args.Errors.Verify({
                    Diagnostic(ERRID.ERR_StdInOptionProvidedButConsoleInputIsNotRedirected).WithLocation(1, 1),
                    Diagnostic(ERRID.WRN_BadSwitch).WithArguments("/nosuchswitch").WithLocation(1, 1)})
            End If

            expectedPaths.Add(Path.Combine(_baseDirectory, "b.vb"))
            Assert.Equal(0, args.ScriptArguments.Length)
            AssertEx.Equal(expectedPaths.ToArray(), args.SourceFiles.Select(Function(file) file.Path))
        End Sub
    End Class
End Namespace
