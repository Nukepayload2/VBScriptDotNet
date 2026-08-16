' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

' Parsing-layer tests for the #! shebang directive on .vbx script files (L1/L2).
' Based on test-plan.md (L1-1..L1-6, L2-1..L2-11) under InternalDevDocs\tasks\shebang-directive.
' Script mode uses TestOptions.Script (SourceCodeKind.Script); regular mode uses TestOptions.Regular.

Imports System.Linq
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports Roslyn.Test.Utilities

Public Class ShebangDirectiveParsingTests
    Inherits BasicTestBase

    Private Const ShebangPath As String = "/opt/vbi-n2fork/vbi"

    Private Shared Function GetShebangDirective(root As CompilationUnitSyntax) As ShebangDirectiveTriviaSyntax
        Return DirectCast(root.GetDirectives().Single(Function(d) d.Kind = SyntaxKind.ShebangDirectiveTrivia), ShebangDirectiveTriviaSyntax)
    End Function

    Private Shared Sub AssertShebangTriviaOnFirstToken(root As CompilationUnitSyntax, path As String)
        Dim firstToken = root.GetFirstToken()
        Dim trivia = firstToken.LeadingTrivia.Single(Function(t) t.Kind = SyntaxKind.ShebangDirectiveTrivia)
        Dim shebang = DirectCast(trivia.GetStructure(), ShebangDirectiveTriviaSyntax)

        Assert.Equal(SyntaxKind.HashToken, shebang.HashToken.Kind())
        Assert.Equal(SyntaxKind.ExclamationToken, shebang.ExclamationToken.Kind())

        Dim skipped = shebang.ExclamationToken.TrailingTrivia.Single(Function(t) t.Kind = SyntaxKind.SkippedTokensTrivia)
        Assert.Equal(path, skipped.ToString())

        Assert.Equal(path, shebang.Content.ToString())
        Assert.Equal(SyntaxKind.StringLiteralToken, shebang.Content.Kind())
    End Sub

    ''' <summary>
    ''' L1-1: A .vbx file starting with #! + code parses the shebang as leading trivia of the first
    ''' real token; the node is HashToken + ExclamationToken with a trailing SkippedTokensTrivia
    ''' carrying the path text.
    ''' </summary>
    <Fact>
    Public Sub Shebang_ParsesAsLeadingTriviaOfFirstToken()
        Dim source = "#!" & ShebangPath & vbCrLf & "Dim x = 1"
        Dim tree = ParseAndVerify(source, TestOptions.Script)
        Dim root = DirectCast(tree.GetRoot(), CompilationUnitSyntax)

        Assert.True(root.ContainsDirectives)
        AssertShebangTriviaOnFirstToken(root, ShebangPath)
    End Sub

    ''' <summary>
    ''' L1-2: root.GetDirectives() discovers the shebang directive.
    ''' </summary>
    <Fact>
    Public Sub Shebang_DiscoverableViaGetDirectives()
        Dim source = "#!" & ShebangPath & vbCrLf & "Dim x = 1"
        Dim tree = ParseAndVerify(source, TestOptions.Script)
        Dim root = DirectCast(tree.GetRoot(), CompilationUnitSyntax)

        Dim directives = root.GetDirectives()
        Assert.Equal(1, directives.Count)
        Assert.Equal(SyntaxKind.ShebangDirectiveTrivia, directives(0).Kind)
        Assert.Equal(ShebangPath, GetShebangDirective(root).Content.ToString())
    End Sub

    ''' <summary>
    ''' L1-3: Line numbers do not drift: shebang on line 1, code on line 2, a parse error on line 3
    ''' reports line 3.
    ''' </summary>
    <Fact>
    Public Sub Shebang_LineNumbersDoNotDrift()
        Dim source = "#!" & ShebangPath & vbCrLf & "Dim x = 1" & vbCrLf & "Dim y = "
        Dim tree = Parse(source, options:=TestOptions.Script)

        Dim err = tree.GetDiagnostics().Single(Function(d) d.Id = "BC30201")
        Assert.Equal(3, err.Location.GetLineSpan().StartLinePosition.Line + 1)
    End Sub

    ''' <summary>
    ''' L1-4: A #! on the second line (after a comment) still parses as a ShebangDirectiveTrivia with
    ''' complete structure; the position error is attached to the '#' token.
    ''' </summary>
    <Fact>
    Public Sub Shebang_OnSecondLine_StillParsesAsShebang()
        Dim source = "' Comment" & vbCrLf & "#!x"
        Dim tree = ParseAndVerify(source, TestOptions.Script,
            Diagnostic(ERRID.ERR_ShebangDirectiveNotOnFirstLine, "#").WithLocation(2, 1))
        Dim root = DirectCast(tree.GetRoot(), CompilationUnitSyntax)

        Dim shebang = GetShebangDirective(root)
        Assert.Equal(SyntaxKind.HashToken, shebang.HashToken.Kind())
        Assert.Equal(SyntaxKind.ExclamationToken, shebang.ExclamationToken.Kind())
        Assert.Equal("x", shebang.Content.ToString())
    End Sub

    ''' <summary>
    ''' L1-5: '#!/... (inside a comment) is a plain comment trivia, not a shebang directive.
    ''' </summary>
    <Fact>
    Public Sub Shebang_InComment_IsPlainComment()
        Dim source = "'#!/usr/bin/env"
        Dim tree = ParseAndVerify(source, TestOptions.Script)
        Dim root = DirectCast(tree.GetRoot(), CompilationUnitSyntax)

        Assert.Empty(root.GetDirectives())
        Dim trivia = root.EndOfFileToken.LeadingTrivia.Single()
        Assert.Equal(SyntaxKind.CommentTrivia, trivia.Kind())
    End Sub

    ''' <summary>
    ''' L1-6: #x is unchanged: a BadDirectiveTrivia with ERR_ExpectedConditionalDirective.
    ''' </summary>
    <Fact>
    Public Sub HashThenNonBang_StillBadDirective()
        Dim tree = ParseAndVerify("#x", TestOptions.Script,
            Diagnostic(ERRID.ERR_ExpectedConditionalDirective, "#").WithLocation(1, 1))
        Dim root = DirectCast(tree.GetRoot(), CompilationUnitSyntax)

        Assert.Equal(SyntaxKind.BadDirectiveTrivia, root.GetDirectives().Single().Kind)
    End Sub

    ''' <summary>
    ''' L2-1: A first-line #! in a script produces no diagnostics.
    ''' </summary>
    <Fact>
    Public Sub Shebang_FirstLineInScript_NoDiagnostics()
        ParseAndVerify("#!cmd", TestOptions.Script)
    End Sub

    ''' <summary>
    ''' L2-2: A #! on the second line reports ERR_ShebangDirectiveNotOnFirstLine (error) at '#'.
    ''' </summary>
    <Fact>
    Public Sub Shebang_SecondLine_ReportsNotOnFirstLine()
        ParseAndVerify("' Comment" & vbCrLf & "#!cmd", TestOptions.Script,
            Diagnostic(ERRID.ERR_ShebangDirectiveNotOnFirstLine, "#").WithLocation(2, 1))

        ParseAndVerify(vbCrLf & "#!cmd", TestOptions.Script,
            Diagnostic(ERRID.ERR_ShebangDirectiveNotOnFirstLine, "#").WithLocation(2, 1))
    End Sub

    ''' <summary>
    ''' L2-3: Leading whitespace before a first-line #! reports NotOnFirstLine at '#'.
    ''' </summary>
    <Fact>
    Public Sub Shebang_WithLeadingWhitespace_ReportsNotOnFirstLine()
        ParseAndVerify("  #!cmd", TestOptions.Script,
            Diagnostic(ERRID.ERR_ShebangDirectiveNotOnFirstLine, "#").WithLocation(1, 3))
    End Sub

    ''' <summary>
    ''' L2-4: '#' and '!' separated by whitespace reports NotOnFirstLine (hashToken.HasTrailingTrivia
    ''' equivalent: the '!' carries leading trivia).
    ''' </summary>
    <Fact>
    Public Sub Shebang_SpaceBetweenHashAndBang_ReportsNotOnFirstLine()
        ParseAndVerify("# !cmd", TestOptions.Script,
            Diagnostic(ERRID.ERR_ShebangDirectiveNotOnFirstLine, "#").WithLocation(1, 1))
    End Sub

    ''' <summary>
    ''' L2-5: A path with or without a leading space after '#!' is accepted with no diagnostics.
    ''' </summary>
    <Fact>
    Public Sub Shebang_LeadingSpaceInPath_NoDiagnostics()
        ParseAndVerify("#! cmd", TestOptions.Script)
        ParseAndVerify("#!cmd", TestOptions.Script)
    End Sub

    ''' <summary>
    ''' L2-6: A #! in a regular (non-script) compilation reports
    ''' ERR_ShebangDirectiveOnlyAllowedInScripts (error) at '!'.
    ''' </summary>
    <Fact>
    Public Sub Shebang_RegularCompilation_ReportsOnlyAllowedInScripts()
        ParseAndVerify("#!cmd", TestOptions.Regular,
            Diagnostic(ERRID.ERR_ShebangDirectiveOnlyAllowedInScripts, "!").WithLocation(1, 2))
    End Sub

    ''' <summary>
    ''' L2-7: A regular non-first-line #! reports both NotOnFirstLine and OnlyAllowedInScripts.
    ''' </summary>
    <Fact>
    Public Sub Shebang_RegularNonFirstLine_ReportsDoubleError()
        ParseAndVerify("' Comment" & vbCrLf & "#!cmd", TestOptions.Regular,
            Diagnostic(ERRID.ERR_ShebangDirectiveNotOnFirstLine, "#").WithLocation(2, 1),
            Diagnostic(ERRID.ERR_ShebangDirectiveOnlyAllowedInScripts, "!").WithLocation(2, 2))
    End Sub

    ''' <summary>
    ''' L2-8: The shebang path can be arbitrary content; the whole line is trivia.
    ''' </summary>
    <Fact>
    Public Sub Shebang_ArbitraryPathContent_NoDiagnostics()
        For Each source In {
            "#!/usr/bin/env -S python",
            "#! /usr/bin/env",
            "#!foo#bar",
            "#!-S"
        }
            ParseAndVerify(source, TestOptions.Script)
        Next
    End Sub

    ''' <summary>
    ''' L2-9: A U+FEFF character before '#!' (in an in-memory source string) is not treated as
    ''' whitespace, so no shebang directive is recognized and the content fails to parse.
    ''' (A real file's BOM is stripped by the file-reading layer, where '#' stays at position 0.)
    ''' </summary>
    <Fact>
    Public Sub Shebang_AfterBomCharacter_NotRecognizedAsDirective()
        Dim source = Convert.ToChar(&HFEFf) & "#!cmd"
        Dim tree = Parse(source, options:=TestOptions.Script)
        Dim root = DirectCast(tree.GetRoot(), CompilationUnitSyntax)

        Assert.DoesNotContain(root.GetDirectives(), Function(d) d.Kind = SyntaxKind.ShebangDirectiveTrivia)

        Dim ids = tree.GetDiagnostics().Select(Function(d) d.Id).ToArray()
        Assert.Contains(ids, Function(id) id = "BC30037" OrElse id = "BC30201")
    End Sub

    ''' <summary>
    ''' L2-10: Inside a disabled #If False region a #! is not a directive (no node, no diagnostics);
    ''' inside an active region a misplaced #! still reports NotOnFirstLine.
    ''' </summary>
    <Fact>
    Public Sub Shebang_InsideDisabledRegion_NoDirective()
        Dim source = "#If False Then" & vbCrLf & "#!x" & vbCrLf & "#End If"
        Dim tree = ParseAndVerify(source, TestOptions.Script)
        Dim root = DirectCast(tree.GetRoot(), CompilationUnitSyntax)

        Assert.DoesNotContain(root.GetDirectives(), Function(d) d.Kind = SyntaxKind.ShebangDirectiveTrivia)
    End Sub

    <Fact>
    Public Sub Shebang_InsideActiveRegion_MisplacedStillReports()
        Dim source = "#If True Then" & vbCrLf & "#!x" & vbCrLf & "#End If"
        ParseAndVerify(source, TestOptions.Script,
            Diagnostic(ERRID.ERR_ShebangDirectiveNotOnFirstLine, "#").WithLocation(2, 1))
    End Sub

    ''' <summary>
    ''' L2-11: Both shebang error codes have Error severity.
    ''' </summary>
    <Fact>
    Public Sub Shebang_ErrorSeverityIsError()
        Dim notFirst = Parse("' c" & vbCrLf & "#!cmd", options:=TestOptions.Script).GetDiagnostics().Single(Function(d) d.Id = "BC37004")
        Assert.Equal(DiagnosticSeverity.Error, notFirst.Severity)

        Dim notScript = Parse("#!cmd", options:=TestOptions.Regular).GetDiagnostics().Single(Function(d) d.Id = "BC37003")
        Assert.Equal(DiagnosticSeverity.Error, notScript.Severity)
    End Sub

End Class
