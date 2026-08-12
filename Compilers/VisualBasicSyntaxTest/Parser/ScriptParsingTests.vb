' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' Parsing-layer tests: the leading "?" on REPL expressions is optional (optional-question-prefix).
' Based on test-plan.md section 3 (P1-P15) under InternalDevDocs\tasks\optional-question-prefix.
' Uses Parse(source, options:=TestOptions.Script) plus ParseAndVerify or direct tree assertions.

Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Public Class ScriptParsingTests
    Inherits BasicTestBase

    Private Shared Function GetFirstStatement(tree As SyntaxTree) As StatementSyntax
        Dim root = DirectCast(tree.GetRoot(), CompilationUnitSyntax)
        Return root.Members(0)
    End Function

    ''' <summary>
    ''' P1: A bare numeric expression 1 + 2 parses as ExpressionStatement(AddExpression) with no BC30801.
    ''' </summary>
    <Fact>
    Public Sub BareNumericExpression_ParsesAsExpressionStatement()
        Dim tree = ParseAndVerify("1 + 2", TestOptions.Script)
        Dim statement = DirectCast(GetFirstStatement(tree), ExpressionStatementSyntax)
        Assert.Equal(SyntaxKind.ExpressionStatement, statement.Kind())
        Assert.Equal(SyntaxKind.AddExpression, statement.Expression.Kind())
        Dim binary = DirectCast(statement.Expression, BinaryExpressionSyntax)
        Assert.Equal(SyntaxKind.NumericLiteralExpression, binary.Left.Kind())
        Assert.Equal(SyntaxKind.PlusToken, binary.OperatorToken.Kind())
        Assert.Equal(SyntaxKind.NumericLiteralExpression, binary.Right.Kind())
    End Sub

    ''' <summary>
    ''' P2: A numeric label 1: remains a LabelStatement; no expression statement is produced.
    ''' </summary>
    <Fact>
    Public Sub NumericLabel_StillParsesAsLabelStatement()
        Dim tree = ParseAndVerify("1:", TestOptions.Script)
        Dim statement = GetFirstStatement(tree)
        Assert.Equal(SyntaxKind.LabelStatement, statement.Kind())
    End Sub

    ''' <summary>
    ''' P3: String concatenation "a" & "b" parses as ExpressionStatement(ConcatenateExpression) with no diagnostics.
    ''' </summary>
    <Fact>
    Public Sub BareStringConcatenation_ParsesAsExpressionStatement()
        Dim tree = ParseAndVerify("""a"" & ""b""", TestOptions.Script)
        Dim statement = DirectCast(GetFirstStatement(tree), ExpressionStatementSyntax)
        Assert.Equal(SyntaxKind.ExpressionStatement, statement.Kind())
        Assert.Equal(SyntaxKind.ConcatenateExpression, statement.Expression.Kind())
    End Sub

    ''' <summary>
    ''' P4: Boolean/Nothing starts (True / Nothing) parse as ExpressionStatement with no diagnostics.
    ''' </summary>
    <Fact>
    Public Sub BareBooleanAndNothing_ParseAsExpressionStatement()
        Dim trueTree = ParseAndVerify("True", TestOptions.Script)
        Dim trueStatement = DirectCast(GetFirstStatement(trueTree), ExpressionStatementSyntax)
        Assert.Equal(SyntaxKind.ExpressionStatement, trueStatement.Kind())
        Assert.Equal(SyntaxKind.TrueLiteralExpression, trueStatement.Expression.Kind())

        Dim nothingTree = ParseAndVerify("Nothing", TestOptions.Script)
        Dim nothingStatement = DirectCast(GetFirstStatement(nothingTree), ExpressionStatementSyntax)
        Assert.Equal(SyntaxKind.ExpressionStatement, nothingStatement.Kind())
        Assert.Equal(SyntaxKind.NothingLiteralExpression, nothingStatement.Expression.Kind())
    End Sub

    ''' <summary>
    ''' P5: A bare identifier (Now / before / MySub) parses as ExpressionStatement(IdentifierName),
    ''' not InvocationExpression (never wrapped as X()).
    ''' </summary>
    <Fact>
    Public Sub BareIdentifier_IsBareExpression_NotInvocation()
        For Each source In {"Now", "before", "MySub"}
            Dim tree = ParseAndVerify(source, TestOptions.Script)
            Dim statement = DirectCast(GetFirstStatement(tree), ExpressionStatementSyntax)
            Assert.Equal(SyntaxKind.ExpressionStatement, statement.Kind())
            Assert.Equal(SyntaxKind.IdentifierName, statement.Expression.Kind())
            Assert.False(TypeOf statement.Expression Is InvocationExpressionSyntax,
                         $"'{source}' must not be wrapped in an invocation expression.")
        Next
    End Sub

    ''' <summary>
    ''' P6: A relational expression x > 5 parses as ExpressionStatement(GreaterThanExpression) with no BC30800.
    ''' </summary>
    <Fact>
    Public Sub BareRelationalExpression_ParsesAsExpressionStatement()
        Dim tree = ParseAndVerify("x > 5", TestOptions.Script)
        Dim statement = DirectCast(GetFirstStatement(tree), ExpressionStatementSyntax)
        Assert.Equal(SyntaxKind.ExpressionStatement, statement.Kind())
        Assert.Equal(SyntaxKind.GreaterThanExpression, statement.Expression.Kind())
    End Sub

    ''' <summary>
    ''' P7: Member access keeps its shape: DateTime.Now / obj.MySub stay InvocationExpression (X() shape),
    ''' not a bare expression (section 2.1(b) baseline).
    ''' </summary>
    <Fact>
    Public Sub MemberAccess_KeepsInvocationShape()
        For Each source In {"DateTime.Now", "obj.MySub"}
            Dim tree = ParseAndVerify(source, TestOptions.Script)
            Dim statement = DirectCast(GetFirstStatement(tree), ExpressionStatementSyntax)
            Assert.Equal(SyntaxKind.ExpressionStatement, statement.Kind())
            Assert.Equal(SyntaxKind.InvocationExpression, statement.Expression.Kind())
        Next
    End Sub

    ''' <summary>
    ''' P8: The explicit ? stays supported: ? Now / ? (1 + 2) remain PrintStatement, unchanged.
    ''' </summary>
    <Fact>
    Public Sub ExplicitQuestion_StillParsesAsPrintStatement()
        Dim nowTree = ParseAndVerify("? Now", TestOptions.Script)
        Dim nowStatement = GetFirstStatement(nowTree)
        Assert.Equal(SyntaxKind.PrintStatement, nowStatement.Kind())

        Dim parenTree = ParseAndVerify("? (1 + 2)", TestOptions.Script)
        Dim parenStatement = GetFirstStatement(parenTree)
        Assert.Equal(SyntaxKind.PrintStatement, parenStatement.Kind())
    End Sub

    ''' <summary>
    ''' P9: For ? x = 5, the PrintStatement expression is EqualsExpression ("=" acts as a comparison).
    ''' </summary>
    <Fact>
    Public Sub QuestionAssignmentSyntax_IsComparisonExpression()
        Dim tree = ParseAndVerify("? x = 5", TestOptions.Script)
        Dim statement = DirectCast(GetFirstStatement(tree), PrintStatementSyntax)
        Assert.Equal(SyntaxKind.PrintStatement, statement.Kind())
        Assert.Equal(SyntaxKind.EqualsExpression, statement.Expression.Kind())
    End Sub

    ''' <summary>
    ''' P10: Assignment invariant: x = 5 is a SimpleAssignmentStatement, not an expression statement.
    ''' </summary>
    <Fact>
    Public Sub Assignment_StillParsesAsAssignmentStatement()
        Dim tree = ParseAndVerify("x = 5", TestOptions.Script)
        Dim statement = GetFirstStatement(tree)
        Assert.Equal(SyntaxKind.SimpleAssignmentStatement, statement.Kind())
        Assert.False(TypeOf statement Is ExpressionStatementSyntax)
    End Sub

    ''' <summary>
    ''' P11: A parenthesized-less Sub call via Call MySub is a CallStatement.
    ''' </summary>
    <Fact>
    Public Sub CallStatement_Unchanged()
        Dim tree = ParseAndVerify("Call MySub", TestOptions.Script)
        Dim statement = GetFirstStatement(tree)
        Assert.Equal(SyntaxKind.CallStatement, statement.Kind())
    End Sub

    ''' <summary>
    ''' P12: A line-continuation bare expression 1 + 2 _ (newline) parses as ExpressionStatement with no diagnostics.
    ''' </summary>
    <Fact>
    Public Sub LineContinuationBareExpression_ParsesAsExpressionStatement()
        Dim tree = ParseAndVerify("1 + 2 _" & vbCrLf, TestOptions.Script)
        Dim statement = DirectCast(GetFirstStatement(tree), ExpressionStatementSyntax)
        Assert.Equal(SyntaxKind.ExpressionStatement, statement.Kind())
        Assert.Equal(SyntaxKind.AddExpression, statement.Expression.Kind())
    End Sub

    ''' <summary>
    ''' P13: Compound-assignment invariants: x += 1 / x &= "s" / x ^= 2 produce their respective AssignmentStatementSyntax subclasses.
    ''' </summary>
    <Fact>
    Public Sub CompoundAssignment_StillParsesAsAssignmentStatement()
        Dim addTree = ParseAndVerify("x += 1", TestOptions.Script)
        Assert.Equal(SyntaxKind.AddAssignmentStatement, GetFirstStatement(addTree).Kind())

        Dim concatTree = ParseAndVerify("x &= ""s""", TestOptions.Script)
        Assert.Equal(SyntaxKind.ConcatenateAssignmentStatement, GetFirstStatement(concatTree).Kind())

        Dim expTree = ParseAndVerify("x ^= 2", TestOptions.Script)
        Assert.Equal(SyntaxKind.ExponentiateAssignmentStatement, GetFirstStatement(expTree).Kind())
    End Sub

    ''' <summary>
    ''' P14: Mid-assignment invariant: Mid(s, 1, 2) = "ab" is a MidAssignmentStatement.
    ''' </summary>
    <Fact>
    Public Sub MidAssignment_StillParsesAsMidAssignmentStatement()
        Dim tree = ParseAndVerify("Mid(s, 1, 2) = ""ab""", TestOptions.Script)
        Dim statement = GetFirstStatement(tree)
        Assert.Equal(SyntaxKind.MidAssignmentStatement, statement.Kind())
    End Sub

    ''' <summary>
    ''' P15: ? . / ? ! dispatch: ? .Foo / ? !bar are not PrintStatement (". " and "!" trigger CanStartConsequenceExpression).
    ''' </summary>
    <Fact>
    Public Sub QuestionDotOrBang_DoesNotParseAsPrintStatement()
        Dim dotTree = Parse("? .Foo", options:=TestOptions.Script)
        Assert.False(TypeOf GetFirstStatement(dotTree) Is PrintStatementSyntax,
                     "? .Foo must not be a PrintStatement.")

        Dim bangTree = Parse("? !bar", options:=TestOptions.Script)
        Assert.False(TypeOf GetFirstStatement(bangTree) Is PrintStatementSyntax,
                     "? !bar must not be a PrintStatement.")
    End Sub

    ''' <summary>
    ''' Regular zero-impact baseline (test-plan section 9): 1 + 2 still reports BC30801 (a label error) under Regular.
    ''' Note: BC30801 is a parse-layer error; only that the error still appears is asserted, not the full error set
    ''' (a residual "+ 2" may carry extra parse diagnostics).
    ''' </summary>
    <Fact>
    Public Sub Regular_BareNumericExpression_StillLabelError()
        Dim tree = Parse("1 + 2", TestOptions.Regular)
        Assert.Contains(tree.GetDiagnostics(), Function(d) d.Id = "BC30801")
    End Sub

    ''' <summary>
    ''' Regular zero-impact baseline (test-plan section 9): ? 1 still reports BC31003 under Regular (ParsePrintStatement errors in Regular).
    ''' </summary>
    <Fact>
    Public Sub Regular_QuestionExpression_StillUnexpected()
        Dim tree = Parse("? 1", TestOptions.Regular)
        Assert.Contains(tree.GetDiagnostics(), Function(d) d.Id = "BC31003")
    End Sub

    ''' <summary>
    ''' Regular zero-impact baseline (test-plan section 9): MySub inside a Sub is still valid under Regular
    ''' (a parenthesized-less Sub call, no parse-layer error).
    ''' </summary>
    <Fact>
    Public Sub Regular_BareSubCall_StillBinds()
        Dim code = "
Class C
    Sub M()
        MySub
    End Sub
    Sub MySub()
    End Sub
End Class"
        ' No parse-layer errors: MySub is a valid bare-identifier member call.
        ParseAndVerify(code, TestOptions.Regular)
    End Sub
End Class
