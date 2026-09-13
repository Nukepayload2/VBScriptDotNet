' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Immutable
Imports System.IO
Imports Microsoft.CodeAnalysis.Emit
Imports Roslyn.Test.Utilities

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests.Emit

    ''' <summary>
    ''' A top-level <c>LabelStatement</c> is an <c>ExecutableStatementSyntax</c> and belongs to the instance
    ''' initializer sequence of the script class like every other top-level statement. It used to be dropped
    ''' outright (<c>SourceMemberContainerTypeSymbol.vb</c>, formerly guarded by an upstream "should be added to the
    ''' initializers" TODO), so a top-level <c>GoTo</c> branched to a target that never made it into the body and
    ''' code generation crashed on the dangling branch (<c>Core\Portable\CodeGen\BasicBlock.cs:325</c>). The
    ''' <c>&lt;Initialize&gt;</c> method is an ordinary async method, so the jump is expected to behave like any other
    ''' <c>GoTo</c>.
    ''' </summary>
    Public Class SubmissionTopLevelLabelTests
        Inherits BasicTestBase

        Private Shared Function CreateSubmissionCompilation(source As String) As VisualBasicCompilation
            Return CreateSubmission(source, options:=TestOptions.DebugDll)
        End Function

        Private Shared Function EmitToMemory(compilation As VisualBasicCompilation) As EmitResult
            Using assemblyStream As New MemoryStream()
                Using pdbStream As New MemoryStream()
                    Return compilation.Emit(assemblyStream, pdbStream)
                End Using
            End Using
        End Function

        Private Shared Function GetErrors(diagnostics As ImmutableArray(Of Diagnostic)) As String()
            Return diagnostics.
                Where(Function(d) d.Severity = DiagnosticSeverity.Error).
                Select(Function(d) d.Id & ": " & d.GetMessage()).
                ToArray()
        End Function

        Private Shared Sub AssertContainsExactlyOneError(diagnostics As ImmutableArray(Of Diagnostic), expectedId As String)
            Dim errors = GetErrors(diagnostics)
            Assert.True(errors.Length = 1 AndAlso errors(0).StartsWith(expectedId), String.Join(" | ", errors))
        End Sub

        ''' <summary>A forward jump without any <c>Await</c> - the shape that already crashed before the fix.</summary>
        <Fact>
        Public Sub ForwardGoToWithoutAwait_Emits()
            Dim compilation = CreateSubmissionCompilation(
                "Dim total = 1" & vbCrLf &
                "GoTo done" & vbCrLf &
                "total = -1000" & vbCrLf &
                "done:" & vbCrLf &
                "System.Console.WriteLine(total)")

            Dim result = EmitToMemory(compilation)

            Assert.True(result.Success, String.Join(" | ", GetErrors(result.Diagnostics)))
        End Sub

        ''' <summary>The shape issue 11 was filed for: the label is followed by an <c>Await</c>.</summary>
        <Fact>
        Public Sub ForwardGoToWithAwait_Emits()
            Dim compilation = CreateSubmissionCompilation(
                "Imports System.Threading.Tasks" & vbCrLf &
                "System.Console.WriteLine(""A"")" & vbCrLf &
                "GoTo skip" & vbCrLf &
                "System.Console.WriteLine(""B"")" & vbCrLf &
                "skip:" & vbCrLf &
                "Await Task.Delay(1)" & vbCrLf &
                "System.Console.WriteLine(""C"")")

            Dim result = EmitToMemory(compilation)

            Assert.True(result.Success, String.Join(" | ", GetErrors(result.Diagnostics)))
        End Sub

        <Fact>
        Public Sub BackwardGoTo_Emits()
            Dim compilation = CreateSubmissionCompilation(
                "Dim i As Integer = 0" & vbCrLf &
                "top:" & vbCrLf &
                "i += 1" & vbCrLf &
                "If i < 3 Then GoTo top" & vbCrLf &
                "System.Console.WriteLine(i)")

            Dim result = EmitToMemory(compilation)

            Assert.True(result.Success, String.Join(" | ", GetErrors(result.Diagnostics)))
        End Sub

        <Fact>
        Public Sub UnusedLabel_Emits()
            Dim compilation = CreateSubmissionCompilation(
                "System.Console.WriteLine(""X"")" & vbCrLf &
                "isolated:")

            Dim result = EmitToMemory(compilation)

            Assert.True(result.Success, String.Join(" | ", GetErrors(result.Diagnostics)))
        End Sub

        ''' <summary>
        ''' A numeric line number is a label like any other, so it takes the same path through the initializer
        ''' sequence as a named label. Line number labels are only tracked for real method bodies
        ''' (<c>Binder_Statements.vb</c>, <c>_containsLineNumberLabel</c>) and the top-level <c>Await</c> walk ignores
        ''' that out parameter, so the existing diagnosis must not change. The duplicated line number is the
        ''' discriminating half: while the statement was dropped, a repeated line number went unnoticed.
        ''' </summary>
        <Fact>
        Public Sub NumericLineNumberLabel_Emits()
            Dim compilation = CreateSubmissionCompilation(
                "Dim i As Integer = 0" & vbCrLf &
                "10:" & vbCrLf &
                "i += 1" & vbCrLf &
                "If i < 3 Then GoTo 10" & vbCrLf &
                "System.Console.WriteLine(i)")

            Dim result = EmitToMemory(compilation)

            Assert.True(result.Success, String.Join(" | ", GetErrors(result.Diagnostics)))

            Dim duplicated = CreateSubmissionCompilation(
                "System.Console.WriteLine(""A"")" & vbCrLf &
                "10:" & vbCrLf &
                "System.Console.WriteLine(""B"")" & vbCrLf &
                "10:" & vbCrLf &
                "System.Console.WriteLine(""C"")")

            Dim duplicateResult = EmitToMemory(duplicated)

            AssertContainsExactlyOneError(duplicateResult.Diagnostics, "BC30094")
            Assert.False(duplicateResult.Success)
        End Sub

        <Fact>
        Public Sub MultipleLabelsAndGoTos_Emits()
            Dim compilation = CreateSubmissionCompilation(
                "Dim total = 0" & vbCrLf &
                "GoTo second" & vbCrLf &
                "first:" & vbCrLf &
                "total += 1" & vbCrLf &
                "second:" & vbCrLf &
                "total += 2" & vbCrLf &
                "System.Console.WriteLine(total)")

            Dim result = EmitToMemory(compilation)

            Assert.True(result.Success, String.Join(" | ", GetErrors(result.Diagnostics)))
        End Sub

        ''' <summary>
        ''' Now that the label statements are bound, a duplicate label is reported instead of being passed over in
        ''' silence. The diagnostic is the one a regular method reports (<c>Binder_Statements.vb:966</c>).
        ''' </summary>
        <Fact>
        Public Sub DuplicateLabel_IsReportedInsteadOfBeingIgnored()
            Dim compilation = CreateSubmissionCompilation(
                "System.Console.WriteLine(""A"")" & vbCrLf &
                "skip:" & vbCrLf &
                "System.Console.WriteLine(""B"")" & vbCrLf &
                "skip:" & vbCrLf &
                "System.Console.WriteLine(""C"")")

            Dim result = EmitToMemory(compilation)

            AssertContainsExactlyOneError(result.Diagnostics, "BC30094")
            Assert.False(result.Success)
        End Sub

        ''' <summary>
        ''' The same duplicate label on a shape that also branches: before the fix this terminated the process in
        ''' code generation instead of reporting anything.
        ''' </summary>
        <Fact>
        Public Sub DuplicateLabelWithGoTo_IsReportedInsteadOfCrashing()
            Dim compilation = CreateSubmissionCompilation(
                "GoTo skip" & vbCrLf &
                "skip:" & vbCrLf &
                "System.Console.WriteLine(""B"")" & vbCrLf &
                "skip:" & vbCrLf &
                "System.Console.WriteLine(""C"")")

            Dim result = EmitToMemory(compilation)

            AssertContainsExactlyOneError(result.Diagnostics, "BC30094")
            Assert.False(result.Success)
        End Sub
    End Class
End Namespace
