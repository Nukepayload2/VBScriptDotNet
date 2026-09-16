' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System
Imports Microsoft.CodeAnalysis.Scripting
Imports Xunit

''' <summary>
''' Runtime function cells of the syntax ledger (test-plan §C.3.F): the <c>Microsoft.VisualBasic</c> runtime
''' functions a script can call. The script reference set of the matrix host is
''' <c>ScriptModeConformance.DefaultOptions</c>, which adds the VB runtime assembly, so the cells write
''' <c>Imports Microsoft.VisualBasic</c> in the script source rather than relying on a project level import - a
''' script has no project, and these cells pin that the runtime namespace is reachable from top level code.
''' <para>
''' Every expected value is a <c>String</c> built without a culture dependent number: the arguments are integers
''' and the only formatting is the VB runtime's own, so the cells read the same under any test culture.
''' </para>
''' </summary>
Public Class ScriptModeRuntimeFunctionTests

    ''' <summary>
    ''' <c>CallByName</c> (<c>Microsoft.VisualBasic.Interaction</c>) called from top level code with all three
    ''' <c>CallType</c> shapes the function takes: a <c>Method</c> call, a <c>Set</c> through a property, and a
    ''' <c>Get</c> back through the same property. The <c>Get</c> reads the value the <c>Set</c> wrote, so a
    ''' <c>CallByName</c> that bound to only one direction shows up as a wrong string.
    ''' </summary>
    <Fact>
    Public Sub TopLevelCallByName_Conform()
        ScriptModeConformance.AssertRuns(
            "Imports Microsoft.VisualBasic" & vbCrLf &
            "Class Greeter" & vbCrLf &
            "    Public Name As String = ""world""" & vbCrLf &
            "    Public Function Hello() As String" & vbCrLf &
            "        Return ""hello "" & Name" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim subject As New Greeter" & vbCrLf &
            "Dim before As String = CallByName(subject, ""Hello"", CallType.Method)" & vbCrLf &
            "CallByName(subject, ""Name"", CallType.Set, ""script"")" & vbCrLf &
            "Dim after As String = CallByName(subject, ""Name"", CallType.Get)" & vbCrLf &
            "Return before & ""/"" & after", "hello world/script")
    End Sub

    ''' <summary>
    ''' <c>LBound</c> / <c>UBound</c> over a two dimensional top level array field. VB arrays are zero based, so
    ''' the bounds are 0 and the declared upper bound, both read per dimension - a call that ignored the
    ''' dimension argument would answer identically for both dimensions, which <c>"0/1/0/2"</c> rules out.
    ''' </summary>
    <Fact>
    Public Sub TopLevelLBoundAndUBound_Conform()
        ScriptModeConformance.AssertRuns(
            "Imports Microsoft.VisualBasic" & vbCrLf &
            "Dim grid(1, 2) As Integer" & vbCrLf &
            "Return LBound(grid, 1) & ""/"" & UBound(grid, 1) & ""/"" & LBound(grid, 2) & ""/"" & UBound(grid, 2)", "0/1/0/2")
    End Sub

    ''' <summary>
    ''' The conditional runtime functions at top level: <c>IIf</c> (both arguments evaluated, unlike the
    ''' <c>If</c> operator), <c>Choose</c> (one based index into the argument list) and <c>Switch</c>(first true
    ''' condition wins). The three expected values come from three different arms, so a function that always
    ''' answered its first argument fails on <c>Choose</c> and <c>Switch</c>.
    ''' </summary>
    <Fact>
    Public Sub TopLevelConditionalRuntimeFunctions_Conform()
        ScriptModeConformance.AssertRuns(
            "Imports Microsoft.VisualBasic" & vbCrLf &
            "Return IIf(True, ""yes"", ""no"") & ""/"" & Choose(2, 10, 20, 30) & ""/"" & Switch(False, ""a"", True, ""b"")", "yes/20/b")
    End Sub

    ''' <summary>
    ''' The VB runtime string and conversion functions the ledger lists as one cell: <c>Len</c>, <c>Mid</c>,
    ''' <c>InStr</c>, <c>Replace</c>, <c>Space</c>, <c>Str</c> and <c>Hex</c>, all called from top level code.
    ''' <c>Mid</c> appears as the function rather than the assignment statement form, and <c>Str</c> keeps its
    ''' leading space for the positive number, which <c>Trim</c> removes - so the cell pins both the runtime call
    ''' and the VB specific formatting it returns.
    ''' </summary>
    <Fact>
    Public Sub TopLevelRuntimeStringFunctions_Conform()
        ScriptModeConformance.AssertRuns(
            "Imports Microsoft.VisualBasic" & vbCrLf &
            "Dim text As String = ""Hello World""" & vbCrLf &
            "Dim head As String = Len(text) & ""/"" & Mid(text, 7) & ""/"" & InStr(text, ""World"")" & vbCrLf &
            "Dim tail As String = Replace(text, ""World"", ""VB"") & ""/"" & Space(2).Length" & vbCrLf &
            "tail &= ""/"" & Str(12).Trim() & ""/"" & Hex(255)" & vbCrLf &
            "Return head & ""/"" & tail", "11/World/7/Hello VB/2/12/FF")
    End Sub

End Class
