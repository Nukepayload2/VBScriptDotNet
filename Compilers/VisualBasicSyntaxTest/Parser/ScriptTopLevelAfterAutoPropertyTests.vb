' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' Parse-layer cells of auto-property-top-level-gate (test-plan.md §二 G1-G9, R1/R1'/R2/R6).
'
' The defect: after a top-level auto-implemented property of a script, the first following entry that starts with
' an identifier (or with AddHandler/RemoveHandler) was rejected with BC30188, because an auto property closes its
' block lazily - only once the next entry arrives and IsPropertyBlock can tell auto from expanded - while BC30188
' is judged during Parse(), one step earlier (Parser.vb:484 parses, :486 links).
'
' Everything in this file is a syntax-tree assertion. The host layer is covered by
' Scripting\VisualBasicTest\ScriptModeConformanceTests.vb, the bound layer by
' Compilers\VisualBasicSemanticTest\Semantics\ScriptTopLevelAutoPropertyTests.vb.

Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Public Class ScriptTopLevelAfterAutoPropertyTests
    Inherits BasicTestBase

    ''' <summary>
    ''' The options the script host parses with (<c>VisualBasicScriptCompiler.vb:23</c>):
    ''' <c>SourceCodeKind.Script</c> plus the latest language version.
    ''' </summary>
    Private Shared ReadOnly Script As New VisualBasicParseOptions(kind:=SourceCodeKind.Script,
                                                                  languageVersion:=LanguageVersion.Latest)

#Region "shape helpers"

    ''' <summary>Parses as script code, asserts the tree carries no diagnostic at all, and hands out its members.</summary>
    Private Shared Function CleanScriptMembers(source As String) As SyntaxList(Of StatementSyntax)
        Dim tree = ParseAndVerify(source, Script)
        Return DirectCast(tree.GetRoot(), CompilationUnitSyntax).Members
    End Function

    Private Shared Function ScriptDiagnostics(source As String) As IEnumerable(Of Diagnostic)
        Return Parse(source, options:=Script).GetDiagnostics()
    End Function

    Private Shared Function RegularDiagnostics(source As String) As IEnumerable(Of Diagnostic)
        Return Parse(source, options:=TestOptions.Regular).GetDiagnostics()
    End Function

    Private Shared Function Describe(diagnostics As IEnumerable(Of Diagnostic)) As String
        Return String.Join(", ", diagnostics.Select(Function(d) $"{d.Id}@{d.Location.SourceSpan.Start}+{d.Location.SourceSpan.Length}"))
    End Function

    ''' <summary>The same reading without the absolute offsets, to compare two sources of different length.</summary>
    Private Shared Function Signature(diagnostics As IEnumerable(Of Diagnostic)) As String
        Return String.Join(", ", diagnostics.Select(Function(d) $"{d.Id}+{d.Location.SourceSpan.Length}"))
    End Function

    Private Shared Sub AssertNoExpectedDeclaration(diagnostics As IEnumerable(Of Diagnostic), cell As String)
        Assert.False(diagnostics.Any(Function(d) d.Id = "BC30188"),
                     cell & ": BC30188 must not be reported, got " & Describe(diagnostics))
    End Sub

    ''' <summary>
    ''' Pins BC30188 on the exact offset <paramref name="marker"/> starts at in <paramref name="source"/> - the
    ''' squiggle of the defect was the successor statement's own first token, so "some BC30188 somewhere" is not
    ''' enough to tell the report points apart.
    ''' </summary>
    Private Shared Sub AssertExpectedDeclarationSquiggles(diagnostics As IEnumerable(Of Diagnostic),
                                                          source As String,
                                                          marker As String,
                                                          cell As String)
        Dim index = source.IndexOf(marker, StringComparison.Ordinal)
        Assert.True(index >= 0, cell & ": the marker is not in the test source: " & marker)
        Assert.True(diagnostics.Any(Function(d) d.Id = "BC30188" AndAlso d.Location.SourceSpan.Start = index),
                    cell & ": expected BC30188 on """ & marker & """ at " & index & ", got " & Describe(diagnostics))
    End Sub

#End Region

#Region "G1/G2/G3/G4/G6: the successor statement starts with an identifier"

    ''' <summary>
    ''' G1: <c>ReadOnly Property P As Integer = 5</c> followed by a call on the property. Zero diagnostics, and the
    ''' successor really is a top-level statement of the compilation unit - an expression statement over an
    ''' invocation - not the incomplete member BC30188 used to leave behind.
    ''' </summary>
    <Fact>
    Public Sub G1_ReadOnlyAutoPropertyWithInitializer_ThenIdentifierCall_IsClean()
        Dim members = CleanScriptMembers(
            "ReadOnly Property P As Integer = 5" & vbCrLf &
            "Console.WriteLine(P)")

        Assert.Equal(2, members.Count)

        Dim propertyStatement = DirectCast(members(0), PropertyStatementSyntax)
        Assert.Equal("P", propertyStatement.Identifier.ToString())
        Assert.NotNull(propertyStatement.Initializer)

        Dim statement = DirectCast(members(1), ExpressionStatementSyntax)
        Dim invocation = DirectCast(statement.Expression, InvocationExpressionSyntax)
        Assert.Equal("Console.WriteLine", invocation.Expression.ToString())
        Assert.Equal("P", invocation.ArgumentList.Arguments(0).ToString())
    End Sub

    ''' <summary>
    ''' G2: no initializer at all - the trigger is the auto property, not the <c>=</c> (<c>P2</c>/<c>P7</c> of the
    ''' baseline). The first successor is an assignment, the shape <c>P7</c> measured as BC30188 on the target.
    ''' </summary>
    <Fact>
    Public Sub G2_AutoPropertyWithoutInitializer_ThenAssignment_IsClean()
        Dim members = CleanScriptMembers(
            "Dim written As Integer" & vbCrLf &
            "ReadOnly Property Q As Integer" & vbCrLf &
            "written = Q + 7" & vbCrLf &
            "Console.WriteLine(written)")

        Assert.Equal(4, members.Count)

        Dim assignment = DirectCast(members(2), AssignmentStatementSyntax)
        Assert.Equal(SyntaxKind.SimpleAssignmentStatement, assignment.Kind())
        Assert.Equal("written", assignment.Left.ToString())
        Assert.Equal("Q + 7", assignment.Right.ToString())
    End Sub

    ''' <summary>
    ''' G3 (parse half): the statements that follow an auto property sit in the same list as the declarations that
    ''' follow them, and a top-level method may read the property.
    ''' </summary>
    <Fact>
    Public Sub G3_AutoPropertyThenStatementThenMethod_IsClean()
        Dim members = CleanScriptMembers(
            "ReadOnly Property P As Integer = 5" & vbCrLf &
            "Console.WriteLine(P)" & vbCrLf &
            "Function Describe() As String" & vbCrLf &
            "    Return ""P="" & P" & vbCrLf &
            "End Function")

        Assert.Equal(3, members.Count)
        Assert.Equal(SyntaxKind.PropertyStatement, members(0).Kind())
        Assert.Equal(SyntaxKind.ExpressionStatement, members(1).Kind())
        Assert.Equal(SyntaxKind.FunctionBlock, members(2).Kind())
    End Sub

    ''' <summary>
    ''' G4 (parse half) and the <c>P8</c> reading: each auto property leaves its own one-entry window, so two of
    ''' them in a row each need the successor after them to be let through.
    ''' </summary>
    <Fact>
    Public Sub G4_TwoAutoPropertiesWithInterleavedStatements_AreClean()
        Dim members = CleanScriptMembers(
            "ReadOnly Property P As Integer = 5" & vbCrLf &
            "Console.WriteLine(P)" & vbCrLf &
            "Property Q As Integer = 7" & vbCrLf &
            "Console.WriteLine(Q)")

        Assert.Equal(4, members.Count)
        Assert.Equal(SyntaxKind.PropertyStatement, members(0).Kind())
        Assert.Equal(SyntaxKind.ExpressionStatement, members(1).Kind())
        Assert.Equal(SyntaxKind.PropertyStatement, members(2).Kind())
        Assert.Equal(SyntaxKind.ExpressionStatement, members(3).Kind())
    End Sub

    ''' <summary>
    ''' The <c>P8</c> shape itself: two auto properties with nothing between them, so the second one is pushed while
    ''' the first is still undetermined and the statement behind them has to be judged through <em>both</em> pending
    ''' blocks. The interleaved cell above only ever has one pending block at a time, which is why this is a cell of
    ''' its own and not a duplicate of it.
    ''' </summary>
    <Fact>
    Public Sub G4_ConsecutiveAutoPropertiesThenStatement_IsClean()
        Dim members = CleanScriptMembers(
            "ReadOnly Property P As Integer = 5" & vbCrLf &
            "ReadOnly Property Q As Integer = 7" & vbCrLf &
            "Console.WriteLine(P + Q)")

        Assert.Equal(3, members.Count)
        Assert.Equal(SyntaxKind.PropertyStatement, members(0).Kind())
        Assert.Equal(SyntaxKind.PropertyStatement, members(1).Kind())
        Assert.Equal(SyntaxKind.ExpressionStatement, members(2).Kind())
    End Sub

    ''' <summary>
    ''' G6: <c>ReadOnly</c> is not part of the trigger (<c>P1</c>), so the writeable spelling has to be clean by the
    ''' same reading - one gate, not one per modifier.
    ''' </summary>
    <Fact>
    Public Sub G6_NonReadOnlyAutoPropertyWithInitializer_IsClean()
        Dim members = CleanScriptMembers(
            "Property P As Integer = 5" & vbCrLf &
            "Console.WriteLine(P)")

        Assert.Equal(2, members.Count)
        Assert.Equal(SyntaxKind.ExpressionStatement, members(1).Kind())
    End Sub

    ''' <summary>
    ''' The bare identifier expression statement after an auto property. <c>P4</c> measured that a <c>?</c> print
    ''' statement was never refused by the window (it does not reach the declaration arms at all), so what is
    ''' asserted here is the bare identifier, which does.
    ''' </summary>
    <Fact>
    Public Sub AutoPropertyThenBareIdentifierExpression_IsClean()
        Dim members = CleanScriptMembers(
            "ReadOnly Property P As Integer = 5" & vbCrLf &
            "P")

        Assert.Equal(2, members.Count)
        Dim statement = DirectCast(members(1), ExpressionStatementSyntax)
        Assert.Equal(SyntaxKind.IdentifierName, statement.Expression.Kind())
    End Sub

    ''' <summary>
    ''' The same entry separated only by a colon (<c>P14</c>): the window is per entry, not per line.
    ''' </summary>
    <Fact>
    Public Sub AutoPropertyColonStatementOnSameLine_IsClean()
        Dim members = CleanScriptMembers(
            "ReadOnly Property P As Integer = 5 : Console.WriteLine(P)")

        Assert.Equal(2, members.Count)
        Assert.Equal(SyntaxKind.ExpressionStatement, members(1).Kind())
    End Sub

#End Region

#Region "G5: an Await initializer"

    ''' <summary>
    ''' G5 (parse half): an <c>Await</c> in the initializer of a top-level auto property is already accepted and
    ''' already ordered (<c>P10</c> = 6); the BC30188 of <c>P11</c> only hid it. With the window fixed the assertion
    ''' is zero diagnostics, not "some other error" - the BC37341/BC36937 family must not show up.
    ''' </summary>
    <Fact>
    Public Sub G5_AwaitInAutoPropertyInitializer_ThenStatement_IsClean()
        Dim members = CleanScriptMembers(
            "ReadOnly Property P As Integer = Await System.Threading.Tasks.Task.FromResult(5)" & vbCrLf &
            "Console.WriteLine(P)")

        Assert.Equal(2, members.Count)
        Assert.Equal(SyntaxKind.PropertyStatement, members(0).Kind())
        Assert.Equal(SyntaxKind.ExpressionStatement, members(1).Kind())

        Dim initializerValue = DirectCast(DirectCast(members(0), PropertyStatementSyntax).Initializer.Value, AwaitExpressionSyntax)
        Assert.Equal(SyntaxKind.AwaitExpression, initializerValue.Kind())
    End Sub

#End Region

#Region "G7: AddHandler / RemoveHandler successors (the second report point)"

    ''' <summary>
    ''' G7 (parse half, <c>Parser.vb:829</c>): <c>AddHandler</c> directly after an auto property. Before the fix
    ''' this took the accessor arm and the declaration context answered BC30188 + BC30205 (<c>P13</c>, report point
    ''' <c>DeclarationContext.vb:147</c>); it is the same window, so it is fixed in the same batch.
    ''' </summary>
    <Fact>
    Public Sub G7_AutoPropertyThenAddHandler_IsClean()
        Dim members = CleanScriptMembers(
            "Event Changed As System.EventHandler" & vbCrLf &
            "ReadOnly Property P As Integer = 5" & vbCrLf &
            "AddHandler Changed, Sub(s As Object, e As System.EventArgs) Console.WriteLine(P)")

        Assert.Equal(3, members.Count)
        Assert.Equal(SyntaxKind.EventStatement, members(0).Kind())
        Assert.Equal(SyntaxKind.PropertyStatement, members(1).Kind())

        Dim addStatement = DirectCast(members(2), AddRemoveHandlerStatementSyntax)
        Assert.Equal(SyntaxKind.AddHandlerStatement, addStatement.Kind())
        Assert.Equal("Changed", addStatement.EventExpression.ToString())
        Assert.Equal("Sub(s As Object, e As System.EventArgs) Console.WriteLine(P)", addStatement.DelegateExpression.ToString())
    End Sub

    ''' <summary>
    ''' G7 second arm (<c>Parser.vb:835</c>): <c>RemoveHandler</c> directly after an auto property. It is a
    ''' separate literal in the dispatcher, so it gets its own cell.
    ''' </summary>
    <Fact>
    Public Sub G7_AutoPropertyThenRemoveHandler_IsClean()
        Dim members = CleanScriptMembers(
            "Event Changed As System.EventHandler" & vbCrLf &
            "ReadOnly Property P As Integer = 5" & vbCrLf &
            "RemoveHandler Changed, Sub(s As Object, e As System.EventArgs) Console.WriteLine(P)")

        Assert.Equal(3, members.Count)
        Dim removeStatement = DirectCast(members(2), AddRemoveHandlerStatementSyntax)
        Assert.Equal(SyntaxKind.RemoveHandlerStatement, removeStatement.Kind())
        Assert.Equal("Changed", removeStatement.EventExpression.ToString())
    End Sub

#End Region

#Region "G8: the two arms AG-F01 left unmeasured"

    ''' <summary>
    ''' G8 arm one, the <c>IntegerLiteralToken</c> arm of the declaration dispatcher (<c>Parser.vb:774</c>,
    ''' <c>:777</c> after this task, which reads <c>IsTopLevelScript</c>): a bare numeric expression statement right
    ''' after an auto property. Measured on the pre-fix published host, this shape answered <c>BC30801</c> ("a
    ''' numeric label must be followed by a colon") while the same statement at the plain top level answered
    ''' <c>BC31003</c> - the arm did not recognise the pending property as top level and fell into the label path.
    ''' Routing the criterion through the context is what carries it over, so this arm is fixed by the same change
    ''' rather than needing one of its own, and the equality of the two readings at the bound layer is pinned in
    ''' <c>ScriptTopLevelAutoPropertyTests.G8_BareNumericStatementAfterAutoProperty_BindsLikeAtThePlainTopLevel</c>.
    ''' The label shape is the control that keeps the arm honest inside the window (<c>1:</c> is still a label).
    ''' </summary>
    <Fact>
    Public Sub G8_AutoPropertyThenBareNumericExpression_IsClean()
        Dim members = CleanScriptMembers(
            "ReadOnly Property P As Integer = 5" & vbCrLf &
            "1 + 2")

        Assert.Equal(2, members.Count)
        Dim statement = DirectCast(members(1), ExpressionStatementSyntax)
        Assert.Equal(SyntaxKind.AddExpression, statement.Expression.Kind())

        Dim labelled = CleanScriptMembers("1:")
        Assert.Equal(SyntaxKind.LabelStatement, labelled(0).Kind())
    End Sub

    ''' <summary>
    ''' G8 arm two, <c>RaiseEvent</c> (<c>Parser.vb:841</c>, <c>:847</c> after this task). Measured on the pre-fix
    ''' published host: with an auto property in front of it the shape answered BC30188 plus BC30205 on the keyword
    ''' (line 6), and without the property - at the plain script top level - it answered the same two diagnostics
    ''' (line 5). The window adds nothing here, because that arm carries no script guard at all, unlike its
    ''' <c>AddHandler</c>/<c>RemoveHandler</c> neighbours, so no part of this task's criterion reaches it.
    ''' <para>
    ''' Decision, recorded rather than left silent: <c>RaiseEvent</c> as a top level statement of a script is
    ''' <em>not</em> in scope for this issue - it is a separate defect of the same dispatcher arm, and loosening it
    ''' would mean adding the guard that <c>:838</c>/<c>:844</c> have, which is the neighbour of the
    ''' <c>Get</c>/<c>Set</c> arms this task is told to leave alone. It is to be filed as its own issue. The
    ''' plain-top-level half is already pinned by
    ''' <c>ScriptModeStatementConformanceTests.TopLevelRaiseEventStatement_IsReported</c>; what this cell adds is the
    ''' measurement that the in-window shape is the same reading, so nobody has to wonder again.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Sub G8_RaiseEventAsSuccessor_IsRefusedForTheSameReasonAsAtThePlainTopLevel()
        Dim withoutPropertySource =
            "Event Changed As System.EventHandler" & vbCrLf &
            "RaiseEvent Changed(Nothing, System.EventArgs.Empty)"
        Dim withPropertySource =
            "Event Changed As System.EventHandler" & vbCrLf &
            "ReadOnly Property P As Integer = 5" & vbCrLf &
            "RaiseEvent Changed(Nothing, System.EventArgs.Empty)"

        Dim withoutProperty = ScriptDiagnostics(withoutPropertySource)
        Dim withProperty = ScriptDiagnostics(withPropertySource)

        AssertExpectedDeclarationSquiggles(withoutProperty, withoutPropertySource, "RaiseEvent", "G8/RaiseEvent control")
        AssertExpectedDeclarationSquiggles(withProperty, withPropertySource, "RaiseEvent", "G8/RaiseEvent in the window")

        Assert.Equal(Signature(withoutProperty), Signature(withProperty))
    End Sub

#End Region

#Region "G9: full parse and incremental reparse must agree"

    Private Shared Function DescribeDiagnostics(tree As SyntaxTree) As String
        Return String.Join(" | ", tree.GetDiagnostics().Select(
            Function(d) $"{d.Id}@{d.Location.SourceSpan.Start}+{d.Location.SourceSpan.Length}"))
    End Function

    ''' <summary>
    ''' The shape of the defect with four more entries behind it, so that an edit can be placed behind the window.
    ''' </summary>
    Private Shared Function PaddedScript() As String
        Return "ReadOnly Property P As Integer = 5" & vbCrLf &
               "Console.WriteLine(P)" & vbCrLf &
               "Dim pad1 As Integer = 1" & vbCrLf &
               "Dim pad2 As Integer = 2" & vbCrLf &
               "Dim pad3 As Integer = 3" & vbCrLf &
               "Dim pad4 As Integer = 4"
    End Function

    ''' <summary>
    ''' Compares an incremental reparse against a full parse of the same new text: identical diagnostics (id and
    ''' absolute span) and an identical member list. The edits used are single-token ones, small enough for
    ''' <c>VisualBasicSyntaxTree.WithChangedText</c> to take the <c>Blender</c> path
    ''' (<c>VisualBasicSyntaxTree.vb:121-126</c>).
    ''' <para>
    ''' What these cells deliberately do not claim is which entries a reparse carries over. That was measured through
    ''' the identity of the green nodes against the base tree, and identity turned out to shift with the history of
    ''' the run - the same source and the same edit read <c>.R</c> in a run of this class alone and <c>..</c> in a
    ''' run of the whole project, and two independent full parses of one text were measured to share a node as well -
    ''' so no assertion here rests on it; diagnostics are what is deterministic. Cases one and three below are the
    ''' ones that put the edit on the successor statement and at the position that decides whether the pending block
    ''' is entered, which is the question judgment six of the README left open; case two is the property side of the
    ''' window and case four only lines the entries behind it up again.
    ''' </para>
    ''' </summary>
    Private Shared Sub AssertIncrementalMatchesFull(source As String, changeSpan As TextSpan, replacement As String)
        Dim oldText = SourceText.From(source)
        Dim oldTree = VisualBasicSyntaxTree.ParseText(oldText, options:=Script)

        Dim changedText = oldText.WithChanges(New TextChange(changeSpan, replacement))
        Assert.Equal(source.Substring(0, changeSpan.Start) & replacement &
                     source.Substring(changeSpan.Start + changeSpan.Length), changedText.ToString())

        Dim incrementalTree = oldTree.WithChangedText(changedText)
        Dim fullTree = VisualBasicSyntaxTree.ParseText(changedText, options:=Script)

        Assert.Equal(DescribeDiagnostics(fullTree), DescribeDiagnostics(incrementalTree))

        Dim fullRoot = DirectCast(fullTree.GetRoot(), CompilationUnitSyntax)
        Dim incrementalRoot = DirectCast(incrementalTree.GetRoot(), CompilationUnitSyntax)
        Assert.Equal(fullRoot.Members.Count, incrementalRoot.Members.Count)
        For index = 0 To fullRoot.Members.Count - 1
            Assert.Equal(fullRoot.Members(index).Kind(), incrementalRoot.Members(index).Kind())
            Assert.Equal(fullRoot.Members(index).Span, incrementalRoot.Members(index).Span)
        Next
    End Sub

    ''' <summary>
    ''' G9 case one: the edit lands on the successor statement, i.e. on the very entry whose legality the defect
    ''' turned on, so the reparse has to judge that entry itself.
    ''' </summary>
    <Fact>
    Public Sub G9_EditOnTheSuccessorStatement_MatchesFullParse()
        Dim source =
            "ReadOnly Property P As Integer = 5" & vbCrLf &
            "Console.WriteLine(P)"
        AssertIncrementalMatchesFull(source,
                                     New TextSpan(source.IndexOf("WriteLine", StringComparison.Ordinal), "WriteLine".Length),
                                     "Write")
    End Sub

    ''' <summary>
    ''' G9 case two: the edit lands on the auto property itself (its initializer), the property side of the window -
    ''' the position where <c>PropertyBlockContext.TryLinkSyntax</c> (<c>:105-127</c>, a different judgement from the
    ''' statement arms) is the one the reparse meets on its way to the statement behind the property.
    ''' </summary>
    <Fact>
    Public Sub G9_EditOnTheAutoPropertyInitializer_MatchesFullParse()
        Dim source =
            "ReadOnly Property P As Integer = 5" & vbCrLf &
            "Console.WriteLine(P)"
        AssertIncrementalMatchesFull(source,
                                     New TextSpan(source.IndexOf("= 5", StringComparison.Ordinal) + 2, 1),
                                     "6")
    End Sub

    ''' <summary>
    ''' G9 case three: the successor is an <c>AddHandler</c> (the second report point) and the edit is a pure
    ''' insertion at the end of the property line - it turns the initializer into the <c>5c</c> Short literal, i.e.
    ''' it changes exactly the token after which the pending block is left or entered, so the reparse asks the new
    ''' criterion the same question a full parse asks. The renamed shape is asserted clean as well, so the comparison
    ''' cannot pass on two failures.
    ''' </summary>
    <Fact>
    Public Sub G9_EditAtTheEndOfTheAutoPropertyLine_MatchesFullParse()
        Dim source =
            "Event Changed As System.EventHandler" & vbCrLf &
            "ReadOnly Property P As Integer = 5" & vbCrLf &
            "AddHandler Changed, Sub(s As Object, e As System.EventArgs) Console.WriteLine(P)"
        Dim endOfPropertyLine = source.IndexOf("= 5", StringComparison.Ordinal) + 3
        AssertIncrementalMatchesFull(source, New TextSpan(endOfPropertyLine, 0), "c")

        CleanScriptMembers(
            "Event Changed As System.EventHandler" & vbCrLf &
            "ReadOnly Property Pc As Integer = 5" & vbCrLf &
            "AddHandler Changed, Sub(s As Object, e As System.EventArgs) Console.WriteLine(Pc)")
    End Sub

    ''' <summary>
    ''' G9 case four: an edit behind the window, so that the comparison covers the shape as a whole being lined up
    ''' again behind a change it is not part of.
    ''' </summary>
    <Fact>
    Public Sub G9_EditFarBehindTheWindow_MatchesFullParse()
        Dim source = PaddedScript()
        AssertIncrementalMatchesFull(source,
                                     New TextSpan(source.LastIndexOf("4", StringComparison.Ordinal), 1),
                                     "5")
    End Sub

#End Region

#Region "R1: ordinary compilations keep every diagnostic they had"

    ''' <summary>
    ''' R1 (one): a statement at the top level of a Regular compilation is still refused by the context with
    ''' BC30689 (<c>ERR_ExecutableAsDeclaration</c>) rather than BC30188: the arm at <c>Parser.vb:812</c> has to
    ''' keep letting the compilation unit through for <c>SourceCodeKind.Regular</c> exactly as it did, which is also
    ''' what <c>ParseErrorTests.BC30188ERR_ExpectedDeclaration</c> pins from the other side.
    ''' </summary>
    <Fact>
    Public Sub R1_RegularTopLevelStatement_StillReportsExecutableAsDeclaration()
        Dim source = "Console.WriteLine(1)"
        Dim diagnostics = RegularDiagnostics(source)

        Assert.True(diagnostics.Any(Function(d) d.Id = "BC30689"), Describe(diagnostics))
        AssertNoExpectedDeclaration(diagnostics, "R1/Regular compilation unit")
    End Sub

    ''' <summary>
    ''' R1 (two): a free-standing statement in the body of a class of a Regular compilation is still BC30188, in
    ''' the shape that shares the arm with the fixed one.
    ''' </summary>
    <Fact>
    Public Sub R1_RegularClassBodyAfterAutoProperty_StillReportsExpectedDeclaration()
        Dim source =
            "Class C1" & vbCrLf &
            "    ReadOnly Property P As Integer = 5" & vbCrLf &
            "    Console.WriteLine(P)" & vbCrLf &
            "End Class"
        AssertExpectedDeclarationSquiggles(RegularDiagnostics(source), source, "Console", "R1/Regular class body")
    End Sub

    ''' <summary>
    ''' R1 (three): the same at the top level of a Regular file, which is the exact shape whose window the fix
    ''' opens - but only for scripts.
    ''' </summary>
    <Fact>
    Public Sub R1_RegularTopLevelAutoPropertyThenStatement_StillReportsExpectedDeclaration()
        Dim source =
            "ReadOnly Property P As Integer = 5" & vbCrLf &
            "Console.WriteLine(P)"
        AssertExpectedDeclarationSquiggles(RegularDiagnostics(source), source, "Console", "R1/Regular file level")
    End Sub

#End Region

#Region "R1': inside the script, but not at its top level"

    ''' <summary>
    ''' R1' (<c>P9</c>): inside a <c>Class</c> of a script the very same shape is still BC30188. This is what proves
    ''' the new criterion really asks for the script compilation unit underneath, instead of switching the whole
    ''' script on with a bare <c>IsScript</c>.
    ''' </summary>
    <Fact>
    Public Sub R1prime_ScriptClassBodyAfterAutoProperty_StillReportsExpectedDeclaration()
        Dim source =
            "Class C1" & vbCrLf &
            "    ReadOnly Property P As Integer = 5" & vbCrLf &
            "    Console.WriteLine(P)" & vbCrLf &
            "End Class"
        AssertExpectedDeclarationSquiggles(ScriptDiagnostics(source), source, "Console", "R1'/class inside a script")
    End Sub

    ''' <summary>
    ''' R1' (<c>P15</c>): inside a <c>Namespace</c> of a script, BC36965 (<c>ERR_NamespaceNotAllowedInScript</c>)
    ''' and BC30188 both stay.
    ''' </summary>
    <Fact>
    Public Sub R1prime_ScriptNamespaceAfterAutoProperty_StillReportsBothDiagnostics()
        Dim source =
            "Namespace N" & vbCrLf &
            "    ReadOnly Property P As Integer = 5" & vbCrLf &
            "    Console.WriteLine(P)" & vbCrLf &
            "End Namespace"
        Dim diagnostics = ScriptDiagnostics(source)

        Assert.True(diagnostics.Any(Function(d) d.Id = "BC36965"), Describe(diagnostics))
        AssertExpectedDeclarationSquiggles(diagnostics, source, "Console", "R1'/namespace inside a script")
    End Sub

    ''' <summary>
    ''' R1' third container: an auto property inside a <c>Module</c> of a script keeps its successor refused, since
    ''' a module body is not the script compilation unit either.
    ''' </summary>
    <Fact>
    Public Sub R1prime_ScriptModuleBodyAfterAutoProperty_StillReportsExpectedDeclaration()
        Dim source =
            "Module M1" & vbCrLf &
            "    ReadOnly Property P As Integer = 5" & vbCrLf &
            "    Console.WriteLine(P)" & vbCrLf &
            "End Module"
        AssertExpectedDeclarationSquiggles(ScriptDiagnostics(source), source, "Console", "R1'/module inside a script")
    End Sub

#End Region

#Region "R2/R6: the neighbours that must not move"

    ''' <summary>
    ''' R2 (<c>O4</c>): an expanded property closes its block on <c>End Property</c>, so it never left a window -
    ''' the reading that made the auto property the only suspect declaration.
    ''' </summary>
    <Fact>
    Public Sub R2_ExpandedPropertyThenStatement_IsClean()
        Dim members = CleanScriptMembers(
            "Property P As Integer" & vbCrLf &
            "    Get" & vbCrLf &
            "        Return 5" & vbCrLf &
            "    End Get" & vbCrLf &
            "End Property" & vbCrLf &
            "Console.WriteLine(P)")

        Assert.Equal(2, members.Count)
        Assert.Equal(SyntaxKind.PropertyBlock, members(0).Kind())
        Assert.Equal(SyntaxKind.ExpressionStatement, members(1).Kind())
    End Sub

    ''' <summary>
    ''' R2 (<c>O5</c>): a top-level field with an initializer is the shape the auto property is supposed to be
    ''' isomorphic to, and it was already clean.
    ''' </summary>
    <Fact>
    Public Sub R2_FieldWithInitializerThenStatement_IsClean()
        Dim members = CleanScriptMembers(
            "Dim fld As Integer = 5" & vbCrLf &
            "Console.WriteLine(fld)")

        Assert.Equal(SyntaxKind.FieldDeclaration, members(0).Kind())
        Assert.Equal(SyntaxKind.ExpressionStatement, members(1).Kind())
    End Sub

    ''' <summary>
    ''' R6: a successor <c>Get</c> still defines the pending property as an expanded one - the accessor arm of
    ''' <c>Parser.vb:843</c> is untouched - and the initializer of an expanded property is still refused with
    ''' BC36714. A criterion that let the statement arm swallow <c>Get</c> would report a clean tree here.
    ''' </summary>
    <Fact>
    Public Sub R6_SuccessorGetStillFormsAnExpandedProperty()
        Dim diagnostics = ScriptDiagnostics(
            "ReadOnly Property P As Integer = 5" & vbCrLf &
            "    Get" & vbCrLf &
            "        Return 5" & vbCrLf &
            "    End Get" & vbCrLf &
            "End Property" & vbCrLf &
            "Console.WriteLine(P)")

        Assert.True(diagnostics.Any(Function(d) d.Id = "BC36714"), Describe(diagnostics))
        AssertNoExpectedDeclaration(diagnostics, "R6/successor Get")

        ' Without the initializer the same shape is simply an expanded property, cleanly, with the statement after
        ' it an ordinary top-level statement.
        CleanScriptMembers(
            "ReadOnly Property P As Integer" & vbCrLf &
            "    Get" & vbCrLf &
            "        Return 5" & vbCrLf &
            "    End Get" & vbCrLf &
            "End Property" & vbCrLf &
            "Console.WriteLine(P)")
    End Sub

    ''' <summary>
    ''' R6: the specifier report point (<c>Parser.vb:875</c>) sits in front of the arm the fix opens and stays
    ''' reachable inside the window - an identifier followed by a token that can start a specifier declaration is
    ''' still BC30188 on the leading identifier, and is not turned into a statement.
    ''' </summary>
    <Fact>
    Public Sub R6_SpecifierReportPointStillFiresInsideTheWindow()
        Dim source =
            "ReadOnly Property P As Integer = 5" & vbCrLf &
            "shadowed name"
        AssertExpectedDeclarationSquiggles(ScriptDiagnostics(source), source, "shadowed", "R6/specifier arm")
    End Sub

    ''' <summary>
    ''' R6: a bare <c>Get</c>/<c>Set</c> at the top level with no property in front of it is still refused - the
    ''' host-layer assertions of <c>ScriptModeParserArmConformanceTests</c> lock the same reading from the other
    ''' side, and the pending-block criterion must not reach them.
    ''' </summary>
    <Fact>
    Public Sub R6_BareTopLevelAccessors_StillReportExpectedDeclaration()
        Dim getSource = "Dim value As Integer = 1" & vbCrLf & "Get" & vbCrLf & "Return value"
        AssertExpectedDeclarationSquiggles(ScriptDiagnostics(getSource), getSource, "Get", "R6/bare Get")

        Dim setSource = "Dim target As Object = Nothing" & vbCrLf & "Set target = New Object" & vbCrLf & "Return target"
        AssertExpectedDeclarationSquiggles(ScriptDiagnostics(setSource), setSource, "Set", "R6/bare Set")
    End Sub

#End Region

End Class
