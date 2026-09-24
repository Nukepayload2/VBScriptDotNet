' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Linq
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Test.Utilities
Imports Roslyn.Test.Utilities

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests

    ''' <summary>
    ''' Row I / queue #7 (issue 17): "top-level statements don't participate in data-flow analysis." They DO - the
    ''' only question is definite assignment (BC42104) for a top-level variable, and VB matches C# there. The C# oracle
    ''' is <c>ScriptSemanticsTests.ERR_UseDefViolation</c> (<c>Compilers\CSharp\Test\Semantic\Semantics\ScriptSemanticsTests.cs:986</c>),
    ''' which asserts CS0165 for a method-local (<c>c</c>) and a block-local (<c>e</c>) but NOT for a top-level variable
    ''' (<c>a</c>/<c>b</c>) - because top-level variables are fields of the synthesized class and fields are always
    ''' default-assigned. VB carries the same split:
    ''' <list type="bullet">
    ''' <item>a top-level <c>Dim s As String</c> is a submission-class field (§2.25(g)), so reading it raises no
    ''' BC42104 - the exact parity to C#'s silent top-level <c>a</c>;</item>
    ''' <item>a <c>Dim s As String</c> inside a top-level-declared <c>Sub</c> is a real local and DOES raise BC42104 -
    ''' the flow analyzer is live, exactly as C# warns for its method/block locals.</item>
    ''' </list>
    ''' ⇒ these cells pin the NON-GAP parity facts (top-level field is exempt from use-def exactly as C# exempts its
    ''' top-level `Program` field variables; a local in a top-level-declared method IS analyzed). They do NOT close the
    ''' narrower block-scoped-local case the issue actually flags (a `Dim` inside a top-level `If`/`For` block is a
    ''' block local, and C# warns CS0165 for that - see `ScriptSemanticsTests.ERR_UseDefViolation` line 998-1001);
    ''' that case is measured separately. The value-type auto-initialization difference is ordinary VB language behavior
    ''' (present identically outside script), not a script-mode divergence.
    ''' </summary>
    Public Class ScriptTopLevelDefiniteAssignmentTests
        Inherits BasicTestBase

        Private Const Bc42104 As String = "BC42104"

        ''' <summary>IDs of the definite-assignment (BC42104) warnings a compilation reports.</summary>
        Private Shared Function UseDefIds(c As VisualBasicCompilation) As String()
            Return c.GetDiagnostics().
                Where(Function(d) d.Id = Bc42104).
                Select(Function(d) d.Id).
                ToArray()
        End Function

        ''' <summary>Parity to C# top-level <c>int a; int b = a;</c> → no CS0165. A top-level reference field is
        ''' default (Nothing), and a field read is never "used before assigned".</summary>
        <Fact>
        Public Sub TopLevel_UnassignedReferenceFieldRead_NoUseDefWarning()
            Dim c = CreateSubmission(
                "Dim s As String" & vbLf &
                "System.Console.WriteLine(s)", parseOptions:=TestOptions.Script)
            Assert.Empty(UseDefIds(c))
        End Sub

        ''' <summary>Parity to C# <c>void F(){ int c; int d = c; }</c> → CS0165 on the local. Proves the top-level
        ''' statements' analysis participates: a real local inside a top-level-declared method IS checked.</summary>
        <Fact>
        Public Sub TopLevelDeclaredMethod_UnassignedReferenceLocalRead_ReportsUseDef()
            Dim c = CreateSubmission(
                "Sub M()" & vbLf &
                "    Dim s As String" & vbLf &
                "    System.Console.WriteLine(s)" & vbLf &
                "End Sub", parseOptions:=TestOptions.Script)
            Assert.Equal({Bc42104}, UseDefIds(c))
        End Sub

        ''' <summary>Cross-submission field persistence: a field declared in an earlier submission stays a field, so
        ''' reading it in a later one is not a use-def violation either (same reason as the single-submission case).</summary>
        <Fact>
        Public Sub CrossSubmission_TopLevelReferenceFieldRead_NoUseDefWarning()
            Dim first = CreateSubmission("Dim s As String", parseOptions:=TestOptions.Script)
            Dim second = CreateSubmission(
                "System.Console.WriteLine(s)",
                parseOptions:=TestOptions.Script,
                previous:=first)
            Assert.Empty(UseDefIds(second))
        End Sub

        ''' <summary>The boundary control: assigning the local first silences BC42104 (the analyzer tracks assignment,
        ''' so the method-local result above is specifically about *unassigned* reads, not about all local reads).</summary>
        <Fact>
        Public Sub TopLevelDeclaredMethod_AssignedReferenceLocalRead_NoUseDefWarning()
            Dim c = CreateSubmission(
                "Sub M()" & vbLf &
                "    Dim s As String = ""x""" & vbLf &
                "    System.Console.WriteLine(s)" & vbLf &
                "End Sub", parseOptions:=TestOptions.Script)
            Assert.Empty(UseDefIds(c))
        End Sub

#Region "Top-level block-scoped local: the gap issue 17 flags - now analyzed (per-statement flow check)"

        ''' <summary>A <c>Dim</c> inside a top-level <c>If</c> block is a block-scoped LOCAL (not a field), and C#
        ''' warns CS0165 for that shape (ScriptSemanticsTests.ERR_UseDefViolation). VB reports BC42104 too now that
        ''' the per-statement top-level flow check lets the definite-assignment family through.</summary>
        <Fact>
        Public Sub TopLevelBlock_UnassignedReferenceLocalRead_ReportsUseDef()
            Dim c = CreateSubmission(
                "If True Then" & vbLf &
                "    Dim s As String" & vbLf &
                "    System.Console.WriteLine(s)" & vbLf &
                "End If", parseOptions:=TestOptions.Script)
            Assert.Equal({Bc42104}, UseDefIds(c))
        End Sub

        ''' <summary>The <c>For</c> body carries the same block-local scope.</summary>
        <Fact>
        Public Sub TopLevelForBlock_UnassignedReferenceLocalRead_ReportsUseDef()
            Dim c = CreateSubmission(
                "For i = 1 To 1" & vbLf &
                "    Dim s As String" & vbLf &
                "    System.Console.WriteLine(s)" & vbLf &
                "Next", parseOptions:=TestOptions.Script)
            Assert.Equal({Bc42104}, UseDefIds(c))
        End Sub

        ''' <summary>Assigning the top-level block local before the read keeps it silent - the warning is about the
        ''' unassigned read, not about top-level block locals per se.</summary>
        <Fact>
        Public Sub TopLevelBlock_AssignedReferenceLocalRead_NoUseDefWarning()
            Dim c = CreateSubmission(
                "If True Then" & vbLf &
                "    Dim s As String = ""x""" & vbLf &
                "    System.Console.WriteLine(s)" & vbLf &
                "End If", parseOptions:=TestOptions.Script)
            Assert.Empty(UseDefIds(c))
        End Sub

#End Region
    End Class
End Namespace
