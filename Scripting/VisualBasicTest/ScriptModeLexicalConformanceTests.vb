' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System
Imports Microsoft.CodeAnalysis.Scripting
Imports Xunit

''' <summary>
''' Lexical cells of the syntax ledger (test-plan §C.3.D). A script file has no container other than its own top
''' level until it declares a type, so each cell writes the construct as top level script code and pins that it is
''' still recognized, still grouped and still bound there - comments do not swallow the statements around them,
''' a type character really names the declared type (the discriminator is <c>Option Strict On</c>, which refuses
''' the late bound access an <c>Object</c> field would need), a <c>c</c> suffixed literal really is a
''' <c>Char</c>, and the conditional compilation and source mapping directives are accepted around top level
''' statements.
''' </summary>
Public Class ScriptModeLexicalConformanceTests

#Region "Comments"

    ''' <summary>
    ''' Both comment forms at the top level: the quote comment (whole line and trailing) and the <c>REM</c>
    ''' statement comment. Neither form is a statement, so the two increments around them have to survive.
    ''' </summary>
    <Fact>
    Public Sub TopLevelComments_Conform()
        ScriptModeConformance.AssertRuns(
            "Dim counted As Integer = 0" & vbCrLf &
            "REM a remark statement comment on its own line" & vbCrLf &
            "counted += 1 ' a trailing quote comment" & vbCrLf &
            "' a whole line quote comment between statements" & vbCrLf &
            "REM another remark" & vbCrLf &
            "counted += 1" & vbCrLf &
            "Return counted", 2)
    End Sub

#End Region

#Region "Type characters"

    ''' <summary>
    ''' Every type character on a top level field, compiled with <c>Option Strict On</c>. This is the
    ''' discriminator, not the arithmetic: a top level <c>Dim</c> without an <c>As</c> clause is bound as
    ''' <c>Object</c> (<c>ScriptModeStatementConformanceTests.TopLevelInferredField_IsObject_Conforms</c>), and
    ''' an <c>Object</c> field would need late bound member access and late bound narrowing, both of which
    ''' <c>Option Strict On</c> rejects (BC30574 / BC30512). A clean submission therefore proves the type
    ''' character - not inference - named each field's type.
    ''' </summary>
    <Fact>
    Public Sub TopLevelTypeCharactersUnderStrictOn_Conform()
        ScriptModeConformance.AssertRuns(
            "Option Strict On" & vbCrLf &
            "Dim name$ = ""abc""" & vbCrLf &
            "Dim count% = 3" & vbCrLf &
            "Dim total& = 10" & vbCrLf &
            "Dim ratio! = 2.0F" & vbCrLf &
            "Dim amount# = 3.0" & vbCrLf &
            "Dim stamp@ = 1.0D" & vbCrLf &
            "Return name$.Length + count% + CInt(ratio!) + CInt(amount#) + CInt(total&) + CInt(stamp@)", 22)
    End Sub

    ''' <summary>
    ''' The same six fields read back through the default <c>Option Strict Off</c>, so the values are visible:
    ''' <c>$</c> String, <c>%</c> Integer, <c>&amp;</c> Long, <c>!</c> Single, <c>#</c> Double,
    ''' <c>@</c> Decimal. The expected string is built from integers only so it does not depend on the culture.
    ''' </summary>
    <Fact>
    Public Sub TopLevelTypeCharacters_Conform()
        ScriptModeConformance.AssertRuns(
            "Dim name$ = ""abc""" & vbCrLf &
            "Dim count% = 3" & vbCrLf &
            "Dim total& = 10" & vbCrLf &
            "Dim ratio! = 1.5F" & vbCrLf &
            "Dim amount# = 2.5" & vbCrLf &
            "Dim stamp@ = 1.5@" & vbCrLf &
            "Return name$ & ""/"" & (count% + 1) & ""/"" & total& & ""/"" & CInt(ratio! * 2) & ""/"" & CInt(amount# * 2) & ""/"" & CInt(stamp@ * 2)", "abc/4/10/3/5/3")
    End Sub

#End Region

#Region "Character literals"

    ''' <summary>
    ''' The <c>c</c> suffix separates a <c>Char</c> literal from a <c>String</c> literal. The discriminator is overload
    ''' resolution - <c>Char</c> and <c>String</c> overloads of one method - because both literals satisfy an
    ''' <c>As Char</c> declaration and only the overload pair tells them apart. The three shapes are the ordinary
    ''' letter, the escaped double quote (<c>""""c</c>) and the unsuffixed <c>String</c> control, so a compiler
    ''' that ignored the suffix would answer <c>SSSS</c> instead of <c>CCSC</c>.
    ''' </summary>
    <Fact>
    Public Sub TopLevelCharacterLiterals_Conform()
        ScriptModeConformance.AssertRuns(
            "Overloads Function Kind(value As Char) As String" & vbCrLf &
            "    Return ""C""" & vbCrLf &
            "End Function" & vbCrLf &
            "Overloads Function Kind(value As String) As String" & vbCrLf &
            "    Return ""S""" & vbCrLf &
            "End Function" & vbCrLf &
            "Dim doubled As Char = """"""""c" & vbCrLf &
            "Return Kind(""a""c) & Kind(""Z""c) & Kind(""a"") & Kind(doubled)", "CCSC")
    End Sub

#End Region

#Region "Conditional compilation directives"

    ''' <summary>
    ''' <c>#Const</c> at the top level defines a conditional compilation constant, and the arms below prove the
    ''' definition took: the first constant is a Boolean and the second is the unevaluated text
    ''' <c>1 + 1</c>, so the <c>AndAlso</c> arm is only taken when the directive both parsed the expression
    ''' and typed it as a Boolean.
    ''' </summary>
    <Fact>
    Public Sub TopLevelConstDirective_Conform()
        ScriptModeConformance.AssertRuns(
            "#Const Feature = True" & vbCrLf &
            "#Const Level = 1 + 1" & vbCrLf &
            "Dim mode As String = ""NONE""" & vbCrLf &
            "#If Feature AndAlso Level = 2 Then" & vbCrLf &
            "    mode = ""ON""" & vbCrLf &
            "#Else" & vbCrLf &
            "    mode = ""OFF""" & vbCrLf &
            "#End If" & vbCrLf &
            "Return mode", "ON")
    End Sub

    ''' <summary>
    ''' The whole <c>#If</c> / <c>#ElseIf</c> / <c>#Else</c> / <c>#End If</c> chain around top level
    ''' statements. The taken arm is the middle one, so neither "always the first arm" nor "always the last arm"
    ''' passes, and the statements of the untaken arms are not part of the submission.
    ''' </summary>
    <Fact>
    Public Sub TopLevelConditionalCompilationDirectives_Conform()
        ScriptModeConformance.AssertRuns(
            "#Const Level = 2" & vbCrLf &
            "Dim text As String = ""none""" & vbCrLf &
            "#If Level = 1 Then" & vbCrLf &
            "    text = ""one""" & vbCrLf &
            "#ElseIf Level = 2 Then" & vbCrLf &
            "    text = ""two""" & vbCrLf &
            "#Else" & vbCrLf &
            "    text = ""other""" & vbCrLf &
            "#End If" & vbCrLf &
            "Return text", "two")
    End Sub

#End Region

#Region "Source mapping and region directives"

    ''' <summary>
    ''' <c>#ExternalSource</c> / <c>#End ExternalSource</c> around top level statements. What the cell pins is
    ''' that the directives are accepted there at all and that the statements between them still bind and run,
    ''' including the one after <c>#End ExternalSource</c>. The stronger reading - the mapped name reaches the
    ''' sequence points, so a stack frame inside the region reports the mapped file - is a probe result, not this
    ''' assertion: the matrix host compiles with <c>emitDebugInformation: false</c>, so there is no sequence point
    ''' table for a stack trace to read (probe: a caught exception thrown inside the region reports
    ''' <c>mapped.vbx</c> under <c>vbi.exe</c>, whose options do emit debug information).
    ''' </summary>
    <Fact>
    Public Sub TopLevelExternalSourceDirective_Conform()
        ScriptModeConformance.AssertRuns(
            "Dim value As Integer = 0" & vbCrLf &
            "#ExternalSource (""mapped.vbx"", 100)" & vbCrLf &
            "value = 4" & vbCrLf &
            "#End ExternalSource" & vbCrLf &
            "value += 1" & vbCrLf &
            "Return value", 5)
    End Sub

    ''' <summary>The unbalanced form of the same directive at the top level: an ordinary directive diagnostic, not
    ''' a crash.</summary>
    <Fact>
    Public Sub TopLevelExternalSourceWithoutEnd_IsReported()
        ScriptModeConformance.AssertReports(
            "#ExternalSource (""mapped.vbx"", 100)" & vbCrLf &
            "Dim value As Integer = 8" & vbCrLf &
            "Return value", "BC30579")
    End Sub

    ''' <summary>
    ''' <c>#Region</c> / <c>#End Region</c> around the top level. The directive is pure trivia, so the observable
    ''' half is that it is accepted there and that the statements it groups still run.
    ''' </summary>
    <Fact>
    Public Sub TopLevelRegionDirective_Conform()
        ScriptModeConformance.AssertRuns(
            "#Region ""Top level region""" & vbCrLf &
            "Dim value As Integer = 8" & vbCrLf &
            "#End Region" & vbCrLf &
            "value += 1" & vbCrLf &
            "Return value", 9)
    End Sub

    ''' <summary>The unterminated <c>#Region</c> at the top level is the matching diagnostic.</summary>
    <Fact>
    Public Sub TopLevelRegionWithoutEnd_IsReported()
        ScriptModeConformance.AssertReports(
            "#Region ""Never closed""" & vbCrLf &
            "Dim value As Integer = 8" & vbCrLf &
            "Return value", "BC30681")
    End Sub

#End Region

End Class
