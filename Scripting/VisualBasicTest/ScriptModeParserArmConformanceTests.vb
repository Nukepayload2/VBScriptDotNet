' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System
Imports System.Linq
Imports Microsoft.CodeAnalysis.Scripting
Imports Xunit

''' <summary>
''' Parser arm and miscellaneous statement cells of the syntax ledger (test-plan §C.3.E). These are the arms of
''' the statement dispatcher that the script top level reaches but an ordinary method body does not: the
''' anachronistic and obsolete statement forms, the empty statement, the keywords the script dialect refuses, the
''' interpolated string and the three XML axes.
''' <para>
''' The top level of a script is the one container an ordinary compilation cannot reach - a submission class has no
''' project file and no base type - and every cell below writes its construct as a top level statement, a top level
''' declaration or a top level field of that submission. Nothing here is wrapped in a <c>Sub</c>/<c>Function</c>:
''' a wrapper body is the nested container, which the compiler test suites already cover.
''' </para>
''' <para>
''' Several cells are negative by nature. A construct the script dialect refuses is pinned by the diagnostic it
''' reports (<c>AssertReports</c>), which is the whole observable surface of a rejected submission; the diagnostic
''' ids were read from a probe of the built <c>vbi.exe</c> and matched against <c>Errors.vb</c>. Where the ledger
''' names a family with more than one member, one cell asserts each member separately rather than one code for
''' three constructs.
''' </para>
''' <para>
''' Everything is in memory and runs inside this process: no file, process, registry or network access. The one
''' extra option set below adds <c>System.Xml.Linq</c>, an assembly already on disk which the metadata layer opens
''' read-only, because the XML literals of the last cell are typed by it.
''' </para>
''' </summary>
Public Class ScriptModeParserArmConformanceTests

    ''' <summary>
    ''' The XML axis cell reads an <c>XElement</c>, whose type lives in <c>System.Xml.Linq</c>; the scripting
    ''' defaults reference neither that assembly nor <c>System.Linq</c>.
    ''' </summary>
    Private Shared ReadOnly XmlOptions As ScriptOptions =
        ScriptModeConformance.DefaultOptions.
            AddReferences(GetType(Enumerable).Assembly).
            AddReferences(GetType(System.Xml.Linq.XElement).Assembly).
            AddImports("System.Linq")

#Region "T050 Empty statements"

    ''' <summary>
    ''' The empty statement in the three shapes the script top level can carry it: the trailing colon that ends a
    ''' line after a top level field declaration, the colon after <c>Else</c> of a single line <c>If</c>, and a
    ''' line whose remaining content is only a comment. Each one has to parse as a statement and none may swallow
    ''' the statement after it, which is why the sum is read rather than one field: a colon that consumed the next
    ''' line would lose <c>second</c>, and a comment line that stayed a statement would lose <c>third</c>.
    ''' </summary>
    <Fact>
    Public Sub TopLevelEmptyStatements_Conform()
        ScriptModeConformance.AssertRuns(
            "Dim first As Integer = 1 :" & vbCrLf &
            "Dim second As Integer = 2" & vbCrLf &
            "If True Then Else :" & vbCrLf &
            "' a line whose remainder is a comment" & vbCrLf &
            "Dim third As Integer = 4" & vbCrLf &
            "Return first + second + third", 7)
    End Sub

#End Region

#Region "T051 Obsolete Get statement"

    ''' <summary>
    ''' The obsolete <c>Get</c> statement reaches <c>Parser.vb:1214</c> (<c>ERR_ObsoleteGetStatement</c>) as soon
    ''' as the statement is a statement of the script's own initializer rather than a direct child of the
    ''' compilation unit: the enclosing <c>If</c> block is a top level statement of the script, not a method body,
    ''' so the container being probed is still the script top level.
    ''' <para>
    ''' The second submission is the bare compilation unit form and pins a different, also real, reading: there
    ''' <c>Get</c> never reaches the obsolete-statement arm. <c>ParseDeclarationStatementInternal</c>
    ''' (<c>Parser.vb:843</c>) routes <c>GetKeyword</c> to the accessor arm, and the declaration context rejects a
    ''' misplaced accessor with BC30188 - unlike <c>AddHandler</c>/<c>RemoveHandler</c>, which do carry a script
    ''' top level guard at <c>Parser.vb:829</c>/<c>:835</c>. Asserting it here keeps the cell from claiming a
    ''' behaviour the top level does not have.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Sub TopLevelObsoleteGetStatement_IsReported()
        ScriptModeConformance.AssertReports(
            "Dim value As Integer = 1" & vbCrLf &
            "If True Then" & vbCrLf &
            "    Get" & vbCrLf &
            "End If" & vbCrLf &
            "Return value", "BC30829")

        ScriptModeConformance.AssertReports(
            "Dim value As Integer = 1" & vbCrLf &
            "Get" & vbCrLf &
            "Return value", "BC30188")
    End Sub

#End Region

#Region "T052 Obsolete Set and Let assignment statements"

    ''' <summary>
    ''' The obsolete assignment prefix is refused with BC30807 (<c>ERR_ObsoleteLetSetNotNeeded</c>,
    ''' <c>Errors.vb:636</c>) on the arm <c>Parser.vb:1095</c> shares between <c>SetKeyword</c> and
    ''' <c>LetKeyword</c> (<c>ParseStatement.vb:1452</c>). Both words are asserted inside a top level <c>If</c>,
    ''' because the arm decides on the keyword and both are refused by it there, and <c>Let</c> is also asserted at
    ''' the bare compilation unit top level, where it is the one of the two that reaches the arm.
    ''' <para>
    ''' <c>Set</c> at the bare top level does not reach the arm for the reason the <c>Get</c> cell above records:
    ''' it is routed to the accessor arm at <c>Parser.vb:846</c> and refused with BC30188, plus BC30205 for the
    ''' text the accessor could not consume. That reading is pinned too, so the cell does not rest on <c>Let</c>
    ''' alone and every assertion about <c>Set</c> is stated as the accessor route rather than the obsolete one.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Sub TopLevelObsoleteSetAndLetAssignment_IsReported()
        ScriptModeConformance.AssertReports(
            "Dim target As Object = Nothing" & vbCrLf &
            "If True Then" & vbCrLf &
            "    Set target = New Object" & vbCrLf &
            "End If" & vbCrLf &
            "Return target", "BC30807")

        ScriptModeConformance.AssertReports(
            "Dim counter As Integer = 0" & vbCrLf &
            "If True Then" & vbCrLf &
            "    Let counter = 3" & vbCrLf &
            "End If" & vbCrLf &
            "Return counter", "BC30807")

        ScriptModeConformance.AssertReports(
            "Dim counter As Integer = 0" & vbCrLf &
            "Let counter = 3" & vbCrLf &
            "Return counter", "BC30807")

        ScriptModeConformance.AssertReports(
            "Dim target As Object = Nothing" & vbCrLf &
            "Set target = New Object" & vbCrLf &
            "Return target", "BC30188")
    End Sub

#End Region

#Region "T053 Namespace declaration"

    ''' <summary>
    ''' A script declares no namespace: <c>Parser.vb:1677</c> marks the keyword with
    ''' <c>ERR_NamespaceNotAllowedInScript</c> (BC36965, <c>Errors.vb:1598</c>) whenever the compilation is a
    ''' script, so the declaration is refused at the script top level rather than at the top level of an ordinary
    ''' compilation. The diagnostic is asserted on its own, so nothing but the <c>Namespace</c> keyword can account
    ''' for it.
    ''' </summary>
    <Fact>
    Public Sub TopLevelNamespaceDeclaration_IsReported()
        ScriptModeConformance.AssertReports(
            "Namespace Group" & vbCrLf &
            "End Namespace" & vbCrLf &
            "Return 0", "BC36965")
    End Sub

#End Region

#Region "T054 MyClass expression"

    ''' <summary>
    ''' <c>MyClass</c> is the other keyword the script dialect refuses outright: <c>Binder_Expressions.vb:2266</c>
    ''' reports <c>ERR_KeywordNotAllowedInScript</c> (BC36966, <c>Errors.vb:1599</c>) for an explicit reference
    ''' from a script class, because a script submission cannot refer to its own type by name. Both positions the
    ''' parser arm at <c>Parser.vb:1158</c> feeds are asserted - the expression statement and the field
    ''' initializer - so the cell does not rest on one of them.
    ''' </summary>
    <Fact>
    Public Sub TopLevelMyClassExpression_IsReported()
        ScriptModeConformance.AssertReports(
            "MyClass.ToString()" & vbCrLf &
            "Return 0", "BC36966")

        ScriptModeConformance.AssertReports(
            "Dim text As String = MyClass.ToString()" & vbCrLf &
            "Return text", "BC36966")
    End Sub

#End Region

#Region "T055 Disable and Enable warning directives"

    ''' <summary>
    ''' The warning directives parse at the script top level through <c>ParseConditional.vb:390</c>
    ''' (<c>ParseWarningDirective</c>) and are pure trivia: the three fields around the directives keep their own
    ''' values, so a directive that had been taken for a statement, or dropped as a bad directive, changes the
    ''' reading. All three shapes are present - <c>#Disable Warning &lt;id&gt;</c>, <c>#Enable Warning &lt;id&gt;</c>
    ''' and a bare <c>#Disable Warning</c>.
    ''' <para>
    ''' The second submission is the discriminator for the arm itself. A malformed id reaches the id validation
    ''' inside <c>ParseWarningDirective</c> (<c>ParseConditional.vb:414</c>) and is refused with BC30468
    ''' (<c>ERR_TypecharNotallowed</c>, <c>Errors.vb:383</c>); a compiler that never called the arm could not
    ''' report it. The effect half of the directive - which warnings a script actually disables - is outside what
    ''' this parser cell can observe: <c>AssertRuns</c> only rejects error severity diagnostics, and the probe of
    ''' <c>vbi.exe</c> showed no warning text for a fall through <c>Function</c> either with or without the
    ''' directive, so no suppression claim is made here.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Sub TopLevelWarningDirectives_Conform()
        ScriptModeConformance.AssertRuns(
            "#Disable Warning BC42353" & vbCrLf &
            "Dim first As String = ""d""" & vbCrLf &
            "#Enable Warning BC42353" & vbCrLf &
            "Dim second As String = ""e""" & vbCrLf &
            "#Disable Warning" & vbCrLf &
            "Dim third As String = ""x""" & vbCrLf &
            "Return first & second & third", "dex")

        ScriptModeConformance.AssertReports(
            "#Disable Warning BC42353%" & vbCrLf &
            "Dim value As Integer = 1" & vbCrLf &
            "Return value", "BC30468")
    End Sub

#End Region

#Region "T056 Anachronistic statements"

    ''' <summary>
    ''' The three anachronistic statements, each alone at the script top level and each on the arm of
    ''' <c>Parser.vb:991</c>/<c>:1223</c> that feeds <c>ParseStatement.vb:487</c>
    ''' (<c>ParseAnachronisticStatement</c>). One assertion per keyword because the arm reports a different
    ''' diagnostic per keyword and the family is the cell: BC30826 for <c>EndIf</c> (<c>Errors.vb:652</c>),
    ''' BC30809 for <c>Wend</c> (<c>Errors.vb:638</c>) and BC30814 for <c>Gosub</c> (<c>Errors.vb:642</c>). Each
    ''' code names its own keyword, so none of the three assertions can be satisfied by another member of the
    ''' family.
    ''' </summary>
    <Fact>
    Public Sub TopLevelAnachronisticStatements_AreReported()
        ScriptModeConformance.AssertReports(
            "EndIf" & vbCrLf &
            "Return 0", "BC30826")

        ScriptModeConformance.AssertReports(
            "Wend" & vbCrLf &
            "Return 0", "BC30809")

        ScriptModeConformance.AssertReports(
            "Gosub 10" & vbCrLf &
            "Return 0", "BC30814")
    End Sub

#End Region

#Region "T057 Error statement"

    ''' <summary>
    ''' The <c>Error</c> statement of <c>Parser.vb:1098</c>/<c>ParseStatement.vb:1569</c> is refused nowhere: it
    ''' binds (<c>Binder_Statements.vb:5347</c>) and the emitter turns the error number into the runtime's
    ''' exception for that number (<c>LocalRewriter_Throw.vb:22-32</c>, the <c>Error</c> statement is the only
    ''' throw whose operand is an <c>Int32</c>). The pinned reading is VB error 13, whose runtime mapping is
    ''' <c>System.InvalidCastException</c>; no other construct at the script top level produces that exception
    ''' type from a literal, and the submission has to survive to the run for the value to come out.
    ''' </summary>
    <Fact>
    Public Sub TopLevelErrorStatement_Conforms()
        ScriptModeConformance.AssertRuns(
            "Dim caught As String = ""none""" & vbCrLf &
            "Try" & vbCrLf &
            "    Error 13" & vbCrLf &
            "Catch ex As System.Exception" & vbCrLf &
            "    caught = ex.GetType().FullName" & vbCrLf &
            "End Try" & vbCrLf &
            "Return caught", "System.InvalidCastException")
    End Sub

#End Region

#Region "T058 Global qualifier"

    ''' <summary>
    ''' The <c>Global</c> qualifier of <c>ParseExpression.vb:239</c> at the script top level, in both positions
    ''' the arm feeds: a bare expression statement (the <c>GlobalKeyword</c> case of the statement dispatcher,
    ''' <c>Parser.vb:1163</c>, and the same case the declaration dispatcher forwards at <c>Parser.vb:849</c>) and a
    ''' field initializer. <c>Global</c> is a reserved keyword here and not a type name - a top level
    ''' <c>Class Global</c> is rejected with BC30183 - so a clean run is only possible if the qualifier was
    ''' recognized and resolved against the root namespace: <c>System.Math</c> and <c>System.String</c> bind, and
    ''' the concatenation is read back.
    ''' </summary>
    <Fact>
    Public Sub TopLevelGlobalQualifier_Conforms()
        ScriptModeConformance.AssertRuns(
            "Dim magnitude As Integer = Global.System.Math.Abs(-7)" & vbCrLf &
            "Global.System.Math.Abs(-7)" & vbCrLf &
            "Dim joined As String = Global.System.String.Concat(""g"", ""l"")" & vbCrLf &
            "Return magnitude & ""/"" & joined", "7/gl")
    End Sub

#End Region

#Region "T059 Interpolated strings"

    ''' <summary>
    ''' The interpolated string of <c>ParseExpression.vb:409</c> (<c>DollarSignDoubleQuoteToken</c>) at the script
    ''' top level, in the four shapes the production has: a hole holding an expression, two adjacent holes, a hole
    ''' with a format specifier and a literal with escaped braces. The expected text separates them - <c>v=3</c>
    ''' from the expression, <c>x4</c> from the adjacent holes, <c>[0002]</c> from the <c>D4</c> specifier and
    ''' <c>a{b}c</c> from the doubled braces - so a compiler that dropped the hole, ignored the specifier or
    ''' consumed the escape answers differently. Only integer formatting is used, so no expected value depends on
    ''' the culture.
    ''' </summary>
    <Fact>
    Public Sub TopLevelInterpolatedStrings_Conform()
        ScriptModeConformance.AssertRuns(
            "Dim count As Integer = 2" & vbCrLf &
            "Dim inner As String = ""x""" & vbCrLf &
            "Dim text As String = $""v={count + 1}""" & vbCrLf &
            "Dim nested As String = $""{inner}{count * 2}""" & vbCrLf &
            "Dim formatted As String = $""[{count:D4}]""" & vbCrLf &
            "Dim escaped As String = $""a{{b}}c""" & vbCrLf &
            "Return text & ""/"" & nested & ""/"" & formatted & ""/"" & escaped",
            "v=3/x4/[0002]/a{b}c")
    End Sub

#End Region

#Region "T060 XML axis dispatch"

    ''' <summary>
    ''' The three axes of <c>ParseExpression.vb:1048</c> used from the script top level, read at statement
    ''' position rather than only in a field initializer: <c>.&lt;name&gt;</c> over the direct children,
    ''' <c>...&lt;name&gt;</c> over the descendants and <c>.@name</c> over the attributes, including a chained
    ''' <c>.&lt;group&gt;.&lt;item&gt;.@id</c>. The direct axis finds one item and the descendant axis finds two, so an
    ''' implementation that mapped both onto one axis cannot produce the reading, and <c>"a"</c> is the direct
    ''' axis' own text rather than the descendant's first item.
    ''' <para>
    ''' The descendant axis is walked with <c>For Each</c> so that the cell needs no <c>System.Linq</c> operator
    ''' to read its count.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Sub TopLevelXmlAxisDispatch_Conforms()
        ScriptModeConformance.AssertRuns(
            "Dim element As System.Xml.Linq.XElement = <root><item id=""1"">a</item><group><item id=""2"">b</item></group></root>" & vbCrLf &
            "Dim deepCount As Integer = 0" & vbCrLf &
            "For Each found In element...<item>" & vbCrLf &
            "    deepCount += 1" & vbCrLf &
            "Next" & vbCrLf &
            "Return element.<item>.Value & ""/"" & element.<group>.<item>.@id & ""/"" & deepCount",
            "a/2/2", XmlOptions)
    End Sub

#End Region

End Class
