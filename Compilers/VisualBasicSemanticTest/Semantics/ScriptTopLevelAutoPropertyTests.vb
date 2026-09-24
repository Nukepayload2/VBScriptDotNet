' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' Bound-layer cells of auto-property-top-level-gate (test-plan.md §二 G10).
'
' G10 is the shape the parse error used to hide: an '<Extension>' auto implemented property at the top level of a
' script, followed by a statement (baseline probe P12 read BC30188 and nothing else, because the parse error
' squiggled the statement before any of it could be bound). With the window fixed, whatever the shape now answers
' has to be recorded as measured - which is what this file is for.

Imports System.Collections.Immutable
Imports Microsoft.CodeAnalysis.Test.Utilities
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports Roslyn.Test.Utilities

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests

    Public Class ScriptTopLevelAutoPropertyTests
        Inherits BasicTestBase

        ''' <summary>The submission options of the script cells of this suite (<c>ScriptSemanticsTests.vb:168</c>).</summary>
        Private Shared Function ScriptOptions() As VisualBasicCompilationOptions
            Return New VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary).
                WithGlobalImports(GlobalImport.Parse("System", "Microsoft.VisualBasic"))
        End Function

        Private Shared Function Describe(diagnostics As IEnumerable(Of Diagnostic)) As String
            Return String.Join(" | ", diagnostics.Select(
                Function(d) $"{d.Id}:{d.Severity}@{d.Location.SourceSpan.Start}+{d.Location.SourceSpan.Length}"))
        End Function

        ''' <summary>The ids and severities only, for comparing two sources whose offsets are not comparable.</summary>
        Private Shared Function DescribeIds(diagnostics As IEnumerable(Of Diagnostic)) As String
            Return String.Join(" | ", diagnostics.OrderBy(Function(d) d.Id).Select(Function(d) $"{d.Id}:{d.Severity}"))
        End Function

        ''' <summary>
        ''' G10: <c>&lt;Extension&gt;</c> on a top level auto implemented property, with a statement behind it, the
        ''' attribute written fully qualified so the reading cannot be an unresolved-name artifact. Measured: the
        ''' BC30188 of the baseline (<c>P12</c>) is gone, and what the shape answers instead is exactly one
        ''' diagnostic, <c>BC30662</c> (<c>ERR_InvalidAttributeUsage2</c>, the attribute is not valid on this
        ''' declaration type) squiggling the attribute. That is the verdict the parse error used to hide, and the
        ''' control below shows it belongs to the attribute and not to the statement.
        ''' </summary>
        <Fact>
        Public Sub G10_ExtensionAutoPropertyWithStatement_BindsWithoutDeclarationError()
            Dim source =
                "<System.Runtime.CompilerServices.Extension>" & vbCrLf &
                "ReadOnly Property P As Integer = 5" & vbCrLf &
                "Console.WriteLine(P)"

            Dim c = CreateSubmission(source, options:=ScriptOptions(), parseOptions:=TestOptions.Script)
            Dim diagnostics = c.GetDiagnostics()

            Assert.DoesNotContain("BC30188", diagnostics.Select(Function(d) d.Id).ToArray())

            ' The measured set, at the measured position: the attribute list alone, nothing about the statement.
            Assert.Equal("BC30662:Error@1+41", Describe(diagnostics))
        End Sub

        ''' <summary>
        ''' G10 control, the same attribute without the statement: this is the reading the shape already had, so the
        ''' cell above cannot be read as "the statement is what the attribute was complaining about" (pitfall
        ''' <c>P-007</c>: a probe has to show the shape it blames is legal on its own). Measured to be the same
        ''' single diagnostic at the same span.
        ''' </summary>
        <Fact>
        Public Sub G10_ExtensionAutoPropertyWithoutStatement_MeasuresTheSameDiagnostics()
            Dim source =
                "<System.Runtime.CompilerServices.Extension>" & vbCrLf &
                "ReadOnly Property P As Integer = 5"

            Dim c = CreateSubmission(source, options:=ScriptOptions(), parseOptions:=TestOptions.Script)

            Assert.DoesNotContain("BC30188", c.GetDiagnostics().Select(Function(d) d.Id).ToArray())
            Assert.Equal("BC30662:Error@1+41", Describe(c.GetDiagnostics()))
        End Sub

        ''' <summary>
        ''' G10 second control, the plain reading of the fixed gate at the bound layer: the same property without
        ''' the attribute, statement included, is clean and the property keeps its initialized value.
        ''' </summary>
        <Fact>
        Public Sub G10_PlainAutoPropertyWithStatement_BindsCleanly()
            Dim c = CreateSubmission(
                "ReadOnly Property P As Integer = 5" & vbCrLf &
                "Console.WriteLine(P)", options:=ScriptOptions(), parseOptions:=TestOptions.Script)

            Assert.Equal("", Describe(c.GetDiagnostics()))
        End Sub

        ''' <summary>
        ''' G8, the bound reading of the <c>IntegerLiteralToken</c> arm (<c>Parser.vb:774</c>, <c>:777</c> after this
        ''' task): a bare numeric expression statement right behind an auto implemented property, with an entry after
        ''' it so that it is not the submission's last one. That arm reads <c>IsTopLevelScript</c>, so routing the
        ''' criterion through the context reaches it as well - which is why its reading is stated here rather than
        ''' left unmeasured. The control is the same file with a <c>Dim</c> in the property's place, i.e. the plain
        ''' top level, where the arm was already taking the script-expression path.
        ''' </summary>
        <Fact>
        Public Sub G8_BareNumericStatementAfterAutoProperty_BindsLikeAtThePlainTopLevel()
            Dim withProperty =
                "ReadOnly Property P As Integer = 5" & vbCrLf &
                "1 + 2" & vbCrLf &
                "System.Console.WriteLine(P)"
            Dim plainTopLevel =
                "Dim P As Integer = 5" & vbCrLf &
                "1 + 2" & vbCrLf &
                "System.Console.WriteLine(P)"

            Dim withPropertyIds = DescribeIds(CreateSubmission(withProperty, options:=ScriptOptions(), parseOptions:=TestOptions.Script).GetDiagnostics())
            Dim plainIds = DescribeIds(CreateSubmission(plainTopLevel, options:=ScriptOptions(), parseOptions:=TestOptions.Script).GetDiagnostics())

            ' Measured, both sides: BC31003 (an expression statement is only allowed at the end of an interactive
            ' submission) and nothing else. The arm behind an auto property now answers exactly what the plain top
            ' level answers, which is the record this cell exists to keep.
            Assert.Equal("BC31003:Error", withPropertyIds)
            Assert.Equal(withPropertyIds, plainIds)
        End Sub

    End Class
End Namespace
