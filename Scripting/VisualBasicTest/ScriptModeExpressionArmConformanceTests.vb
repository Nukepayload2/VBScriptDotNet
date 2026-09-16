' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System
Imports Xunit

''' <summary>
''' The expression arms of the syntax ledger that no spec heading names (test-plan §C.3.E): <c>NameOf</c> and the
''' null propagating conditional access operator. Both dispatch from <c>ParseExpression.vb</c> rather than from the
''' statement dispatcher, and neither has a heading in <c>expressions.md</c> - the ledger rows come from the
''' implementation arm, not from the spec tree.
''' <para>
''' Each construct is asserted in both containers. A bare top level statement or declaration of the submission is the
''' top level container; a method of a type the script itself declares is the nested one. Both have to be script
''' tests here: <c>ParseNameOf</c> and the <c>ConditionalAccessExpression</c> arm are reached from a script unit the
''' same way an ordinary compilation reaches them, so a compiler test says nothing about the script container's
''' members - the submission fields and the nested type the cells below write into.
''' </para>
''' <para>
''' The conditional access cells are written through the three consequence forms the arm accepts - <c>?.</c>,
''' <c>?!</c> and <c>?(</c> - and every form is read twice, once with a live receiver and once with a <c>Nothing</c>
''' one. The pair is the discriminator: the live half pins the consequence expression parsed after the <c>?</c>, the
''' other half pins the propagation, because a compiler that parsed the operator without propagating throws
''' <c>NullReferenceException</c> instead of answering.
''' </para>
''' <para>
''' Everything is in memory and runs inside this process: no file, process, registry or network access.
''' </para>
''' </summary>
Public Class ScriptModeExpressionArmConformanceTests

#Region "T061 NameOf expressions"

    ''' <summary>
    ''' <c>NameOf</c> (<c>ParseExpression.vb:347</c> -&gt; <c>ParseNameOf</c>, declared at <c>:671</c>) at the script top
    ''' level, in the three argument shapes the production has: a type (<c>NameOf(System.String)</c>), a member
    ''' (<c>NameOf(System.Console.WriteLine)</c>) and an entity the submission declares itself (<c>NameOf(total)</c>,
    ''' a top level field). A constant that kept the argument expression instead of its name, or that resolved the
    ''' argument and returned its value, reads back differently from all three.
    ''' </summary>
    <Fact>
    Public Sub TopLevelNameOfExpression_Conforms()
        ScriptModeConformance.AssertRuns(
            "Dim total As Integer = 7" & vbCrLf &
            "Dim typeName As String = NameOf(System.String)" & vbCrLf &
            "Dim memberName As String = NameOf(System.Console.WriteLine)" & vbCrLf &
            "Dim fieldName As String = NameOf(total)" & vbCrLf &
            "Return typeName & ""/"" & memberName & ""/"" & fieldName",
            "String/WriteLine/total")
    End Sub

    ''' <summary>
    ''' The same production inside a method body of a type the script declares, naming both an ambient type and the
    ''' enclosing method, so the reading proves the argument was resolved in the nested scope rather than in the
    ''' submission.
    ''' </summary>
    <Fact>
    Public Sub NestedNameOfExpression_Conforms()
        ScriptModeConformance.AssertRuns(
            "Class Namer" & vbCrLf &
            "    Public Function Describe() As String" & vbCrLf &
            "        Return NameOf(System.String) & ""/"" & NameOf(Describe)" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim subject As New Namer" & vbCrLf &
            "Return subject.Describe()", "String/Describe")
    End Sub

#End Region

#Region "T062 Null propagating conditional access"

    ''' <summary>
    ''' The conditional access operator at the script top level. The dispatcher is <c>ParseExpression.vb:415</c> for
    ''' the position where the <c>?</c> starts the term and <c>:481</c> for the postfix position, both guarded by
    ''' <c>CanStartConsequenceExpression</c> (<c>:512</c>) and both feeding
    ''' <c>SyntaxFactory.ConditionalAccessExpression</c> (<c>:422</c> / <c>:502</c>). All three consequence forms are
    ''' present: <c>?.</c> over a member, <c>?!</c> over a dictionary key and <c>?(</c> over an argument list.
    ''' </summary>
    <Fact>
    Public Sub TopLevelNullPropagatingAccess_Conforms()
        ScriptModeConformance.AssertRuns(
            "Dim found As String = ""abc""" & vbCrLf &
            "Dim missing As String = Nothing" & vbCrLf &
            "Dim upperMissing As String = missing?.ToUpper()" & vbCrLf &
            "Dim upperFound As String = found?.ToUpper()" & vbCrLf &
            "Dim table As New System.Collections.Generic.Dictionary(Of String, String)" & vbCrLf &
            "table.Add(""k"", ""v"")" & vbCrLf &
            "Dim emptyTable As System.Collections.Generic.Dictionary(Of String, String) = Nothing" & vbCrLf &
            "Dim bangFound As String = table?!k" & vbCrLf &
            "Dim bangMissing As String = emptyTable?!k" & vbCrLf &
            "Dim parenFound As String = table?(""k"")" & vbCrLf &
            "Return If(upperMissing Is Nothing, ""nil"", upperMissing) & ""/"" & upperFound & ""/"" & " &
            "bangFound & ""/"" & If(bangMissing Is Nothing, ""nil"", bangMissing) & ""/"" & parenFound",
            "nil/ABC/v/nil/v")
    End Sub

    ''' <summary>
    ''' The same operator inside method bodies of a type the script declares, over a parameter (<c>?.</c>) and over a
    ''' parameter of the dictionary type (<c>?!</c>). The <c>Nothing</c> halves are the propagation reading; the live
    ''' halves prove the consequence expression still binds against a receiver that only arrives at run time.
    ''' </summary>
    <Fact>
    Public Sub NestedNullPropagatingAccess_Conforms()
        ScriptModeConformance.AssertRuns(
            "Class Probe" & vbCrLf &
            "    Public Function Observe(text As String) As String" & vbCrLf &
            "        Dim upper As String = text?.ToUpper()" & vbCrLf &
            "        Return If(upper Is Nothing, ""nil"", upper)" & vbCrLf &
            "    End Function" & vbCrLf &
            "    Public Function Lookup(table As System.Collections.Generic.Dictionary(Of String, String)) As String" & vbCrLf &
            "        Dim value As String = table?!k" & vbCrLf &
            "        Return If(value Is Nothing, ""nil"", value)" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim subject As New Probe" & vbCrLf &
            "Dim table As New System.Collections.Generic.Dictionary(Of String, String)" & vbCrLf &
            "table.Add(""k"", ""v"")" & vbCrLf &
            "Return subject.Observe(Nothing) & ""/"" & subject.Observe(""abc"") & ""/"" & " &
            "subject.Lookup(table) & ""/"" & subject.Lookup(Nothing)",
            "nil/ABC/v/nil")
    End Sub

#End Region

End Class
