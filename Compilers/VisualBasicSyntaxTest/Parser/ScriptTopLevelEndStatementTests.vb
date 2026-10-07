' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' Parse-layer cells of script-mode-end-statement (test-plan.md §一 ES-A1..ES-A5, §二 ES-B1/ES-B4/ES-B5).
'
' The defect: the EndKeyword arm of ParseDeclarationStatementInternal (Parser.vb:829) sent every End at declaration
' level to ParseGroupEndStatement, which reads "End" as a group end statement with an optional keyword. With a
' statement terminator after it there is no group keyword, so it reported ERR_UnrecognizedEnd (BC30678) on a
' statement it had already parsed as the stop-or-end statement. The method body dispatcher has always judged the
' two readings through ParseEndStatement, so the arm now reuses it for the top level of a script (the IsTopLevelScript
' guard, written like the AddHandler arm above it).
'
' Everything here is a syntax tree assertion. The bound layer is ScriptEndStatementTests.vb, the emission layer is
' CodeGenStopOrEnd.EndStatement_SubmissionWithEnd_EmitsTheEndAppCall, and the host layer is
' Scripting\VisualBasicTest\ScriptModeStatementConformanceTests.vb.

Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Public Class ScriptTopLevelEndStatementTests
    Inherits BasicTestBase

    ''' <summary>
    ''' The options the script host parses with (VisualBasicScriptCompiler.vb): SourceCodeKind.Script plus the latest
    ''' language version.
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

    ''' <summary>
    ''' The End of a script at top level is the stop-or-end statement, which is a StopOrEndStatementSyntax of kind
    ''' EndStatement - not the EndBlockStatementSyntax family (End If, End Sub, ...), whose kinds are one per block.
    ''' </summary>
    Private Shared Sub AssertIsStopOrEndStatement(statement As StatementSyntax, cell As String)
        Assert.True(statement.Kind() = SyntaxKind.EndStatement,
                    cell & ": the End has to be a stop-or-end statement, got " & statement.Kind().ToString())
        Assert.IsType(Of StopOrEndStatementSyntax)(statement)
        Assert.IsNotType(Of EndBlockStatementSyntax)(statement)
    End Sub

#End Region

#Region "ES-A1..ES-A5: the bare End of a script is the stop-or-end statement"

    ''' <summary>
    ''' ES-A1: a bare top level End between two statements. Zero diagnostics, and the End really is the stop-or-end
    ''' statement rather than an incomplete block end (the node kind is what separates the two readings; the parse
    ''' before the fix produced the same node with BC30678 attached to it).
    ''' </summary>
    <Fact>
    Public Sub ES_A1_BareTopLevelEndBetweenStatements_IsClean()
        Dim members = CleanScriptMembers(
            "System.Console.WriteLine(""A1"")" & vbCrLf &
            "End" & vbCrLf &
            "System.Console.WriteLine(""A2"")")

        Assert.Equal(3, members.Count)
        Assert.Equal(SyntaxKind.ExpressionStatement, members(0).Kind())
        AssertIsStopOrEndStatement(members(1), "ES-A1")
        Assert.Equal(SyntaxKind.ExpressionStatement, members(2).Kind())
    End Sub

    ''' <summary>
    ''' ES-A2: the same statement terminated by a colon on one line, the second of the two terminators
    ''' ParseEndStatement asks about (CanFollowStatementButIsNotSelectFollowingExpression). Zero diagnostics and the
    ''' End is still the stop-or-end statement, so the statement behind it is a member of its own.
    ''' </summary>
    <Fact>
    Public Sub ES_A2_BareTopLevelEndFollowedByAColon_IsClean()
        Dim members = CleanScriptMembers("End : System.Console.WriteLine(""A2"")")

        Assert.Equal(2, members.Count)
        AssertIsStopOrEndStatement(members(0), "ES-A2")
        Assert.Equal(SyntaxKind.ExpressionStatement, members(1).Kind())
    End Sub

    ''' <summary>
    ''' ES-A3: a trailing comment is trivia, so what follows the End is still a statement terminator and the reading
    ''' has to be the same one.
    ''' </summary>
    <Fact>
    Public Sub ES_A3_BareTopLevelEndWithTrailingComment_IsClean()
        Dim members = CleanScriptMembers("End ' trailing comment")

        Assert.Equal(1, members.Count)
        AssertIsStopOrEndStatement(members(0), "ES-A3")
    End Sub

    ''' <summary>
    ''' ES-A4: the End is the last entry of the compilation unit, so the token behind it is the end of file - the
    ''' third terminator. Zero diagnostics, not even the BC30678 the group end route reported for it.
    ''' </summary>
    <Fact>
    Public Sub ES_A4_BareTopLevelEndAsLastEntry_IsClean()
        Dim members = CleanScriptMembers("End")

        Assert.Equal(1, members.Count)
        AssertIsStopOrEndStatement(members(0), "ES-A4")
    End Sub

    ''' <summary>
    ''' ES-A5: an End inside a top level If block. Nothing about this cell is changed by the task - the inner End
    ''' goes through the method body dispatcher either way - but it is the parse side control that shows both gates
    ''' open at once, and it pins that the closing "End If" is still the group end statement.
    ''' </summary>
    <Fact>
    Public Sub ES_A5_TopLevelIfBlockWithInnerEnd_IsClean()
        Dim members = CleanScriptMembers(
            "If True Then" & vbCrLf &
            "    End" & vbCrLf &
            "End If")

        Assert.Equal(1, members.Count)

        Dim ifBlock = DirectCast(members(0), MultiLineIfBlockSyntax)
        Assert.Equal(1, ifBlock.Statements.Count)
        AssertIsStopOrEndStatement(ifBlock.Statements(0), "ES-A5/inner End")

        ' The closing "End If" belongs to the block node, and it is still a group end statement, not a stop-or-end
        ' statement - the group end route of the guard is the one that produced it.
        Assert.NotNull(ifBlock.EndIfStatement)
        Assert.Equal(SyntaxKind.EndIfStatement, ifBlock.EndIfStatement.Kind())
        Assert.IsType(Of EndBlockStatementSyntax)(ifBlock.EndIfStatement)
    End Sub

#End Region

#Region "ES-B1/ES-B4/ES-B5: the ordinary compilation and the real group ends are unaffected"

    ''' <summary>
    ''' ES-B1: a bare top level End of an ordinary (Regular) compilation is still BC30678. IsTopLevelScript is
    ''' Context.AcceptsScriptTopLevelStatement, which is False for SourceCodeKind.Regular, so the EndKeyword arm takes
    ''' the group end route exactly as it did - this cell is the lock that the new branch did not leak into ordinary
    ''' input. It also pins that the diagnostic is the only one, i.e. the reading was not turned into a different
    ''' error.
    ''' </summary>
    <Fact>
    Public Sub ES_B1_RegularTopLevelBareEnd_StillReportsUnrecognizedEnd()
        Dim diagnostics = RegularDiagnostics("End")

        Assert.Equal("BC30678@0+3", Describe(diagnostics))
    End Sub

    ''' <summary>
    ''' ES-B4: real group end statements at the top level of a script keep the diagnostics the declaration context
    ''' table gives them (DeclarationContext.RecoverFromMismatchedEnd, which this task does not touch). "End If" and
    ''' "End Sub" are the two arms of that table named in the plan.
    ''' <para>
    ''' How the "before" reading was established: the working tree was already carrying the fix, so the reading could
    ''' not be taken from the pre-change compiler in this session. What the cell pins instead is (a) the exact
    ''' diagnostic set, taken from the running compiler, and (b) its equality with the reading of the same shape as a
    ''' Regular file - the control the plan allows for that case, because IsTopLevelScript is False there and the
    ''' Regular route through the arm is textually the pre-change one. The two shapes reach ParseGroupEndStatement
    ''' from ParseEndStatement instead of from the arm, which peeks the same token and consumes none, so the state at
    ''' entry is the same state the arm used to hand it.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Sub ES_B4_ScriptTopLevelGroupEnds_KeepTheirOwnDiagnostics()
        Dim scriptEndIf = ScriptDiagnostics("End If")
        Dim scriptEndSub = ScriptDiagnostics("End Sub")
        Dim regularEndIf = RegularDiagnostics("End If")
        Dim regularEndSub = RegularDiagnostics("End Sub")

        ' Measured: the mismatched end table (ERR_EndIfNoMatchingIf, ERR_InvalidEndSub), on the statement alone.
        Assert.Equal("BC30087@0+6", Describe(scriptEndIf))
        Assert.Equal("BC30429@0+7", Describe(scriptEndSub))

        ' The structural control: the same reading as the unchanged Regular route.
        Assert.Equal(Signature(regularEndIf), Signature(scriptEndIf))
        Assert.Equal(Signature(regularEndSub), Signature(scriptEndSub))
    End Sub

    ''' <summary>
    ''' ES-B5: the fix withdraws a false report rather than exchanging it for another code. The bare top level End of
    ''' a script reports neither the parse error it used to (BC30678) nor the class library error (BC30615), which is
    ''' the shape a "swap one gate for the other" repair would leave behind.
    ''' </summary>
    <Fact>
    Public Sub ES_B5_ScriptTopLevelEnd_ReportsNeitherDiagnostic()
        Dim ids = ScriptDiagnostics("End").Select(Function(d) d.Id).ToArray()

        Assert.DoesNotContain("BC30678", ids)
        Assert.DoesNotContain("BC30615", ids)
        Assert.Empty(ids)
    End Sub

#End Region

End Class
