' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' Bound-layer cells of script-mode-end-statement (test-plan.md §一 ES-A6..ES-A10).
'
' The second gate: a script submission is compiled with OutputKind.DynamicallyLinkedLibrary, so
' Binder_Statements.BindEndStatement answered every End that reached the binder with BC30615
' (ERR_EndDisallowedInDllProjects). The gate is widened to a submission as well as an application, because a
' submission is compiled as a library for the host to load but is the entry point of its own process.
'
' ES-A10 is the narrowness lock of that widening: a script tree compiled by an ordinary (non submission)
' compilation with a DLL output still reports BC30615. Were the criterion DeclarationKind.Script instead of
' IsSubmission, that cell would go green, so it is failing the cell that means the product change went too far.
'
' Everything here is compile only - no cell runs anything (End lowers to ProjectData.EndApp(), which would end the
' test process). The emission layer is CodeGenStopOrEnd.EndStatement_SubmissionWithEnd_EmitsTheEndAppCall in
' VisualBasicEmitTest.

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests

    Public Class ScriptEndStatementTests
        Inherits BasicTestBase

        ''' <summary>The submission options of the script cells of this suite (ScriptSemanticsTests.vb:168).</summary>
        Private Shared Function ScriptOptions() As VisualBasicCompilationOptions
            Return New VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary).
                WithGlobalImports(GlobalImport.Parse("System", "Microsoft.VisualBasic"))
        End Function

        Private Shared Function Describe(diagnostics As IEnumerable(Of Diagnostic)) As String
            Return String.Join(" | ", diagnostics.Select(
                Function(d) $"{d.Id}:{d.Severity}@{d.Location.SourceSpan.Start}+{d.Location.SourceSpan.Length}"))
        End Function

        Private Shared Sub AssertClean(compilation As VisualBasicCompilation, cell As String)
            Dim reading = Describe(compilation.GetDiagnostics())
            Assert.True(reading = "", cell & ": the submission has to be diagnostic free, got " & reading)
        End Sub

#Region "ES-A6..ES-A9: a submission binds End cleanly"

        ''' <summary>
        ''' ES-A6: the top level bare End of a submission - this cell exercises both gates at once, the parse arm
        ''' (Parser.vb:829) and the widened binder criterion.
        ''' </summary>
        <Fact>
        Public Sub ES_A6_SubmissionTopLevelEnd_IsDiagnosticFree()
            Dim c = CreateSubmission(
                "System.Console.WriteLine(""A6"")" & vbCrLf &
                "End", options:=ScriptOptions(), parseOptions:=TestOptions.Script)

            AssertClean(c, "ES-A6")
        End Sub

        ''' <summary>
        ''' ES-A7: the End inside a top level method of a submission - the shape the host suite used to pin as BC30615
        ''' (ScriptModeStatementConformanceTests.EndInsideMethod_IsReported before the rewrite), and the one the binder
        ''' gate is about. The call behind the End is unreachable at run time, which this cell does not exercise.
        ''' </summary>
        <Fact>
        Public Sub ES_A7_SubmissionEndInsideMethod_IsDiagnosticFree()
            Dim c = CreateSubmission(
                "Sub Finish()" & vbCrLf &
                "    System.Console.WriteLine(""A7"")" & vbCrLf &
                "    End" & vbCrLf &
                "End Sub" & vbCrLf &
                "Finish()", options:=ScriptOptions(), parseOptions:=TestOptions.Script)

            AssertClean(c, "ES-A7")
        End Sub

        ''' <summary>
        ''' ES-A8: the End inside a lambda body. The binder reaches a lambda body through the same statement binder,
        ''' so the gate is the same one; the cell is separate because the lambda is the third host the issue measured
        ''' (the probe e-end-in-lambda.vbx).
        ''' </summary>
        <Fact>
        Public Sub ES_A8_SubmissionEndInsideLambda_IsDiagnosticFree()
            Dim c = CreateSubmission(
                "Dim act As System.Action = Sub()" & vbCrLf &
                "    End" & vbCrLf &
                "End Sub" & vbCrLf &
                "act()", options:=ScriptOptions(), parseOptions:=TestOptions.Script)

            AssertClean(c, "ES-A8")
        End Sub

        ''' <summary>
        ''' ES-A9: the second submission of a chain. IsSubmission is True for every submission of the chain, which is
        ''' what makes this the same gate rather than a per-first-submission accident; the identifier in the first
        ''' submission is read to show the chain is really linked.
        ''' </summary>
        <Fact>
        Public Sub ES_A9_ChainedSubmissionEnd_IsDiagnosticFree()
            Dim first = CreateSubmission(
                "Dim x = 1", options:=ScriptOptions(), parseOptions:=TestOptions.Script)
            AssertClean(first, "ES-A9/first submission")

            Dim second = CreateSubmission(
                "System.Console.WriteLine(x)" & vbCrLf &
                "End", options:=ScriptOptions(), parseOptions:=TestOptions.Script, previous:=first)

            AssertClean(second, "ES-A9/second submission")
        End Sub

#End Region

#Region "ES-A10: a non submission compilation of a script tree is still a library"

        ''' <summary>
        ''' ES-A10, the narrowness lock: the same source as ES-A7, parsed as a script but compiled by an ordinary
        ''' compilation with a DLL output. There is no submission here, so IsSubmission is False and the criterion
        ''' degenerates word for word to the original "Not OutputKind.IsApplication()" - BC30615 has to stay.
        ''' <para>
        ''' The comparison with the submission reading is the point: same source, same parse options, same references,
        ''' only the compilation factory differs. If this cell ever goes green, the criterion was widened to the script
        ''' <em>tree</em> (DeclarationKind.Script) instead of the submission, and the fix has to go back to
        ''' Compilation.IsSubmission rather than the cell being relaxed.
        ''' </para>
        ''' </summary>
        <Fact>
        Public Sub ES_A10_NonSubmissionScriptTreeWithDllOutput_StillReportsEndDisallowedInDllProjects()
            Dim source =
                "Sub Finish()" & vbCrLf &
                "    End" & vbCrLf &
                "End Sub"

            Dim compilation = VisualBasicCompilation.Create(
                GetUniqueName(),
                {Parse(source, options:=TestOptions.Script)},
                {MscorlibRef_v4_0_30316_17626, MsvbRef_v4_0_30319_17929},
                TestOptions.ReleaseDll)

            Assert.False(compilation.IsSubmission, "ES-A10: the compilation under test must not be a submission")

            Dim ids = compilation.GetDiagnostics().Select(Function(d) d.Id).ToArray()
            Assert.Contains("BC30615", ids)

            ' The submission-shaped control of the same source, for the comparison the doc comment describes.
            Dim submission = CreateSubmission(source, options:=ScriptOptions(), parseOptions:=TestOptions.Script)
            Assert.DoesNotContain("BC30615", submission.GetDiagnostics().Select(Function(d) d.Id).ToArray())

            Assert.Equal("", Describe(submission.GetDiagnostics()))
            ' Measured: exactly the one class library diagnostic, on the End keyword (offset 18 of the source above:
            ' "Sub Finish()" plus the newline plus the four spaces of the indentation).
            Assert.Equal("BC30615:Error@18+3", Describe(compilation.GetDiagnostics()))
        End Sub

#End Region

    End Class
End Namespace
