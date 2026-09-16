' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System
Imports Microsoft.CodeAnalysis.Scripting
Imports Xunit

''' <summary>
''' Expression and operator cells of the syntax ledger (test-plan §C.3.C) in the top level container. The top level
''' is the one container an ordinary compilation cannot reach: the submission class has no project file and no base
''' type, so the same operator text is bound there as a member of a type the script itself declares. Every cell
''' below writes the operator inside a top level statement or a top level field initializer - never inside a
''' <c>Sub</c>/<c>Function</c> body of the submission class, which would be the nested container instead.
''' <para>
''' Every expected value is discriminating: it can only come out of the operator the cell is about. Addition,
''' subtraction, multiplication, division, exponentiation and the shifts all run on the same three and four,
''' which reads 7 / -1 / 12 / "7/6" / "8/9" / "24/-1" - a cell whose operator was mis-bound answers one of the
''' neighboring readings, not its own. The unary <c>+</c> cell cannot be discriminated on an <c>Integer</c>
''' operand at all (it is the identity there), so it uses a type whose <c>Operator +</c> the script declares by
''' hand, plus a negative literal for the sign half. <c>Like</c> and <c>TypeOf...Is</c> each pin a true and a
''' false branch, <c>/</c> is separated from <c>\</c> by reading both through a doubling that only one of them
''' survives, and <c>&lt;&lt;</c> is separated from a multiplication by two to the same power by the one operand
''' where the two disagree: <c>1 &lt;&lt; 31</c> wraps to <c>Integer.MinValue</c>.
''' </para>
''' <para>
''' The readings were taken from the built <c>vbi.exe</c> (the same sources with the result written to the console
''' instead of returned) before they were written here, so no expected value is inferred. Everything is in memory:
''' the uniform host of <see cref="ScriptModeConformance"/> reads no file, starts no process and touches neither
''' the registry nor the network.
''' </para>
''' </summary>
Public Class ScriptModeOperatorConformanceTests

#Region "Parenthesized Expressions"

    ''' <summary>
    ''' Parentheses in a top level field initializer, where they are the only thing that decides the grouping: the
    ''' unparenthesized reading of the same digits is <c>14</c> and the parenthesized one is <c>20</c>, and the
    ''' doubly parenthesized expression adds a third grouping of its own. A compiler that parsed the parentheses as
    ''' trivia (or dropped the ones around a nested expression) answers <c>14/14/6</c> or <c>14/14/12</c>.
    ''' </summary>
    <Fact>
    Public Sub TopLevelParenthesizedExpressions_Conform()
        ScriptModeConformance.AssertRuns(
            "Dim width As Integer = 2 + 3 * 4" & vbCrLf &
            "Dim grouped As Integer = (2 + 3) * 4" & vbCrLf &
            "Dim nested As Integer = ((1 + 1) * (2 + 1))" & vbCrLf &
            "Return grouped & ""/"" & width & ""/"" & nested", "20/14/6")
    End Sub

#End Region

#Region "TypeOf...Is Expressions"

    ''' <summary>
    ''' Both answers of the operator in one cell, over three receivers the script declares itself: the instance's own
    ''' type and its base type answer <c>True</c>, a type it is not an instance of answers <c>False</c>, and a boxed
    ''' <c>Object</c> is tested against the type it holds and against one it does not. The false branch is what a
    ''' compiler that folded the test to its left operand's declared type would get wrong, and the base type arm is
    ''' what a compiler comparing exact types only would drop.
    ''' </summary>
    <Fact>
    Public Sub TopLevelTypeOfIsExpressions_Conform()
        ScriptModeConformance.AssertRuns(
            "Class Widget" & vbCrLf &
            "End Class" & vbCrLf &
            "Class Gadget" & vbCrLf &
            "    Inherits Widget" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim plain As New Widget" & vbCrLf &
            "Dim fancy As New Gadget" & vbCrLf &
            "Dim boxed As Object = ""text""" & vbCrLf &
            "Dim matches As String = """"" & vbCrLf &
            "If TypeOf plain Is Widget Then matches &= ""W""" & vbCrLf &
            "If TypeOf plain Is Gadget Then matches &= ""G""" & vbCrLf &
            "If TypeOf fancy Is Gadget Then matches &= ""g""" & vbCrLf &
            "If TypeOf fancy Is Widget Then matches &= ""w""" & vbCrLf &
            "If TypeOf boxed Is String Then matches &= ""S""" & vbCrLf &
            "If TypeOf boxed Is Integer Then matches &= ""I""" & vbCrLf &
            "Return matches", "WgwS")
    End Sub

#End Region

#Region "Dictionary Member Access Expressions"

    ''' <summary>
    ''' The <c>!name</c> form at the top level, read from a type the script declares the dictionary of. The
    ''' discriminator is a top level field that carries the same name as the key (<c>alpha = 99</c> against the
    ''' entry <c>11</c>): an implementation that resolved <c>!alpha</c> as a member or variable reference instead of
    ''' as the string key the operator spells answers <c>99</c>, and a compiler that dropped the <c>!</c> token
    ''' cannot bind the expression at all.
    ''' </summary>
    <Fact>
    Public Sub TopLevelDictionaryMemberAccess_Conforms()
        ScriptModeConformance.AssertRuns(
            "Dim ages As New System.Collections.Generic.Dictionary(Of String, Integer)" & vbCrLf &
            "Dim alpha As Integer = 99" & vbCrLf &
            "ages(""alpha"") = 11" & vbCrLf &
            "ages(""beta"") = 22" & vbCrLf &
            "Return ages!alpha & ""/"" & ages!beta", "11/22")
    End Sub

#End Region

#Region "Unary Plus Operator"

    ''' <summary>
    ''' <c>+</c> on an <c>Integer</c> is the identity, so the cell discriminates on a type whose unary plus the
    ''' script declares by hand: the operator adds <c>100</c> to a <c>5</c>, and the <c>105</c> can only come from
    ''' the operator being invoked (a compiler that evaluated the operand alone answers <c>5</c>). The second reading
    ''' covers the sign half over a negative literal, where a <c>+</c> read as a <c>-</c> answers <c>5</c>.
    ''' </summary>
    <Fact>
    Public Sub TopLevelUnaryPlusOperator_Conforms()
        ScriptModeConformance.AssertRuns(
            "Structure Marker" & vbCrLf &
            "    Public Amount As Integer" & vbCrLf &
            "    Public Shared Operator +(m As Marker) As Marker" & vbCrLf &
            "        Return New Marker With {.Amount = m.Amount + 100}" & vbCrLf &
            "    End Operator" & vbCrLf &
            "End Structure" & vbCrLf &
            "Dim plain As New Marker With {.Amount = 5}" & vbCrLf &
            "Dim plussed As Marker = +plain" & vbCrLf &
            "Dim negative As Integer = -5" & vbCrLf &
            "Return plussed.Amount & ""/"" & (+negative)", "105/-5")
    End Sub

#End Region

#Region "Subtraction Operator"

    ''' <summary>
    ''' One top level subtraction read in both operand orders. The magnitudes separate it from every neighboring
    ''' operator: <c>7 - 3</c> is <c>4</c> against <c>10</c> for addition, <c>21</c> for multiplication and
    ''' <c>-4</c> for the reversed order, which is the second reading - a compiler that made the operator commutative
    ''' (or that answered the sign of the first operand) cannot produce both.
    ''' </summary>
    <Fact>
    Public Sub TopLevelSubtractionOperator_Conforms()
        ScriptModeConformance.AssertRuns(
            "Dim a As Integer = 7" & vbCrLf &
            "Dim b As Integer = 3" & vbCrLf &
            "Return a - b & ""/"" & (b - a)", "4/-4")
    End Sub

#End Region

#Region "Multiplication Operator"

    ''' <summary>
    ''' <c>3 * 4</c> at the top level answers <c>12</c>, which no neighboring operator produces: addition reads
    ''' <c>7</c>, subtraction <c>-1</c>, division <c>"7/6"</c> and exponentiation <c>81</c>. A binding that fell back
    ''' to the addition of the same operands, or that returned one operand (<c>3</c> or <c>4</c>), is caught.
    ''' </summary>
    <Fact>
    Public Sub TopLevelMultiplicationOperator_Conforms()
        ScriptModeConformance.AssertRuns(
            "Dim a As Integer = 3" & vbCrLf &
            "Dim b As Integer = 4" & vbCrLf &
            "Return a * b", 12)
    End Sub

#End Region

#Region "Division Operators"

    ''' <summary>
    ''' Both division operators on the same operands, each read through the doubling that exposes its own result:
    ''' <c>7 / 2</c> is the floating <c>3.5</c>, which doubles to <c>7</c>, while <c>7 \ 2</c> is the integer
    ''' <c>3</c>, which doubles to <c>6</c>. A compiler that mapped the two onto one operator answers <c>"7/8"</c>
    ''' (the <c>\</c> half narrowing <c>3.5</c> into an <c>Integer</c>) or <c>"6/6"</c> (the <c>/</c> half
    ''' answering <c>3.0</c>), and neither may appear.
    ''' </summary>
    <Fact>
    Public Sub TopLevelDivisionOperators_Conform()
        ScriptModeConformance.AssertRuns(
            "Dim total As Integer = 7" & vbCrLf &
            "Dim parts As Integer = 2" & vbCrLf &
            "Dim floating As Double = total / parts" & vbCrLf &
            "Dim integral As Integer = total \ parts" & vbCrLf &
            "Return floating * 2 & ""/"" & integral * 2", "7/6")
    End Sub

#End Region

#Region "Exponentiation Operator"

    ''' <summary>
    ''' <c>^</c> read in both operand orders, so a commutative or a multiplication-shaped binding is excluded: the
    ''' pair is <c>8</c> and <c>9</c>, where the multiplication of the same operands reads <c>6</c> both ways and a
    ''' single wrong order collapses the two readings onto one another. The result is <c>Double</c> by definition of
    ''' the operator, so each half is narrowed explicitly instead of letting the concatenation pick a format.
    ''' </summary>
    <Fact>
    Public Sub TopLevelExponentiationOperator_Conforms()
        ScriptModeConformance.AssertRuns(
            "Dim radix As Integer = 2" & vbCrLf &
            "Dim exponent As Integer = 3" & vbCrLf &
            "Return CInt(radix ^ exponent) & ""/"" & CInt(exponent ^ radix)", "8/9")
    End Sub

#End Region

#Region "Like Operator"

    ''' <summary>
    ''' The pattern operator with both answers: <c>*</c> matches the tail and <c>?</c> is one character, so the
    ''' first test matches and the second, which asks the same input to be a three character word ending in
    ''' <c>d</c>, does not. A compiler that folded the operator to <c>True</c> answers <c>"True/True/True"</c> and
    ''' one that folded it to <c>False</c> answers the mirror image; the third reading covers the empty match at the
    ''' start of a longer text.
    ''' </summary>
    <Fact>
    Public Sub TopLevelLikeOperator_Conforms()
        ScriptModeConformance.AssertRuns(
            "Dim matched As Boolean = ""abc"" Like ""a*c""" & vbCrLf &
            "Dim missed As Boolean = ""abc"" Like ""a?d""" & vbCrLf &
            "Dim text As String = ""VB Script""" & vbCrLf &
            "Return matched & ""/"" & missed & ""/"" & (text Like ""Script*"")", "True/False/False")
    End Sub

#End Region

#Region "Shift Operators"

    ''' <summary>
    ''' Both shifts at the top level. <c>6 &lt;&lt; 2</c> is <c>24</c> and <c>6 &gt;&gt; 2</c> is <c>1</c>, so a
    ''' compiler that swapped the two directions is caught. The second reading is the one that separates the shift
    ''' from a multiplication by the same power of two: <c>-1 &gt;&gt; 1</c> is the sign propagating <c>-1</c>, where
    ''' division by two reads <c>0</c> (<c>\</c>) or <c>-0.5</c> (<c>/</c>). The third reading is the overflow end -
    ''' <c>1 &lt;&lt; 31</c> wraps to <c>Integer.MinValue</c>, which no multiplication by 2^31 of an <c>Integer</c>
    ''' can produce.
    ''' </summary>
    <Fact>
    Public Sub TopLevelShiftOperators_Conform()
        ScriptModeConformance.AssertRuns(
            "Dim value As Integer = 6" & vbCrLf &
            "Dim shifted As Integer = value << 2" & vbCrLf &
            "Dim arithmetic As Integer = -1 >> 1" & vbCrLf &
            "Dim wrapped As Integer = 1 << 31" & vbCrLf &
            "Return shifted & ""/"" & arithmetic & ""/"" & wrapped", "24/-1/-2147483648")
    End Sub

#End Region

End Class
