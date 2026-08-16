' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Reflection
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports Xunit

Public Class InteractiveSessionTests

    <Fact>
    Public Async Function Fields() As Task
        Dim s = Await VisualBasicScript.
            RunAsync("Dim x As Integer = 1").
            ContinueWith("Dim y As Integer = 2").
            ContinueWith("?x + y")

        Assert.Equal(3, s.ReturnValue)
    End Function

    <Fact>
    Public Async Function Imports_CrossSubmission() As Task
        Dim options = ScriptOptions.Default.AddReferences(GetType(System.Text.StringBuilder).Assembly)
        Dim s = Await VisualBasicScript.
            RunAsync("Imports System.Text", options).
            ContinueWith("Dim builder = New StringBuilder()", options).
            ContinueWith("? builder.GetType().FullName", options)

        Assert.Equal("System.Text.StringBuilder", s.ReturnValue)
    End Function

    <Fact>
    Public Async Function Imports_DoNotReplaceInheritedOptionsImports() As Task
        Dim options = ScriptOptions.Default.
            AddReferences(GetType(Console).Assembly, GetType(System.Text.StringBuilder).Assembly).
            AddImports("System")

        Dim s = Await VisualBasicScript.
            RunAsync("Dim consoleType = GetType(Console)", options).
            ContinueWith("Imports System.Text").
            ContinueWith("? GetType(Console).FullName")

        Assert.Equal("System.Console", s.ReturnValue)
    End Function

    <Fact>
    Public Sub ScriptOptionsImports_AreNotCachedAcrossScripts()
        Dim systemOptions = ScriptOptions.Default.
            AddReferences(GetType(Version).Assembly).
            AddImports("System")

        Assert.Equal("Version", VisualBasicScript.EvaluateAsync(
"? New Version(1, 2).GetType().Name",
systemOptions).Result)

        Dim systemTextOptions = ScriptOptions.Default.
            AddReferences(GetType(System.Text.StringBuilder).Assembly).
            AddImports("System.Text")

        Assert.Equal("StringBuilder", VisualBasicScript.EvaluateAsync(
"? New StringBuilder().GetType().Name",
systemTextOptions).Result)
    End Sub

    <Fact>
    Public Async Function PreviousSubmissions_Declarations() As Task
        Dim s = Await VisualBasicScript.
            RunAsync("
Function AddOne(value As Integer) As Integer
    Return value + 1
End Function
").
            ContinueWith("
Class Counter
    Public Value As Integer
End Class
").
            ContinueWith("
Module Helpers
    Public Function Twice(value As Integer) As Integer
        Return value * 2
    End Function
End Module
").
            ContinueWith("
Delegate Function Transformer(value As Integer) As Integer
").
            ContinueWith("
Dim counter = New Counter With {.Value = 3}
Dim transformer As Transformer = AddressOf AddOne
? Helpers.Twice(transformer(counter.Value))
")

        Assert.Equal(8, s.ReturnValue)
    End Function

    <Fact>
    Public Sub StatementExpressions_LineContinuation()
        Dim source = "
?1 _
"
        Assert.Equal(1, VisualBasicScript.EvaluateAsync(source).Result)
    End Sub

    <Fact>
    Public Sub StatementExpressions_IntLiteral()
        Dim source = "
?1
"
        Assert.Equal(1, VisualBasicScript.EvaluateAsync(source).Result)
    End Sub

    <Fact>
    Public Sub StatementExpressions_Nothing()
        Dim source = "
?  Nothing
"

        Assert.Null(VisualBasicScript.EvaluateAsync(source).Result)
    End Sub

    <Fact, WorkItem(10856, "DevDiv_Projects/Roslyn")>
    Public Sub IfStatement()
        Dim source = "
Dim x As Integer
If (True)
   x = 5
Else
   x = 6
End If

?x + 1
"

        Assert.Equal(6, VisualBasicScript.EvaluateAsync(source).Result)
    End Sub

    <Fact>
    Public Sub AnonymousTypes_TopLevel_MultipleSubmissions()
        Dim script = VisualBasicScript.Create("
Option Infer On
Dim a = New With { .f = 1 }
").ContinueWith("
Option Infer On
Dim b = New With { Key .f = 1 }
").ContinueWith("
Option Infer On
Dim c = New With { .F = 222 }
Dim d = New With { Key .F = 777 }

? (a.GetType() Is c.GetType()).ToString() _
    & "" "" & (a.GetType() Is b.GetType()).ToString() _
    & "" "" & (b.GetType() is d.GetType()).ToString()
")
        Assert.Equal("True False True", script.EvaluateAsync().Result)
    End Sub

    <Fact>
    Public Sub AnonymousTypes_TopLevel_MultipleSubmissions2()
        Dim script = VisualBasicScript.Create("
Option Infer On
Dim a = Sub()
        End Sub
").ContinueWith("
Option Infer On
Dim b = Function () As Integer
            Return 0
        End Function
").ContinueWith("
Option Infer On
Dim c = Sub()
        End Sub
Dim d = Function () As Integer
            Return 0
        End Function
? (a.GetType() is c.GetType()).ToString() _
    & "" "" & (a.GetType() is b.GetType()).ToString() _ 
    & "" "" & (b.GetType() is d.GetType()).ToString()
")

        Assert.Equal("True False True", script.EvaluateAsync().Result)
    End Sub

    <Fact>
    Public Sub CompilationChain_Accessibility()
        ' Submissions have internal and protected access to one another.
        Dim state1 = VisualBasicScript.RunAsync(
"Friend Class C1
End Class
Protected X As Integer
")
        Dim compilation1 = state1.Result.Script.GetCompilation()
        compilation1.VerifyDiagnostics()

        Dim state2 = state1.ContinueWith(
"Friend Class C2
    Inherits C1
End Class
")
        Dim compilation2 = state2.Result.Script.GetCompilation()
        compilation2.VerifyDiagnostics()
        Dim c2C2 = DirectCast(lookupMember(compilation2, "Submission#1", "C2"), INamedTypeSymbol)
        Dim c2C1 = c2C2.BaseType
        Dim c2X = lookupMember(compilation1, "Submission#0", "X")
        Assert.True(compilation2.IsSymbolAccessibleWithin(c2C1, c2C2))
        Assert.True(compilation2.IsSymbolAccessibleWithin(c2C2, c2C1))
        Assert.True(compilation2.IsSymbolAccessibleWithin(c2X, c2C2))  ' access not enforced among submission symbols

        Dim state3 = state2.ContinueWith(
"Friend Class C3
    Inherits C2
End Class
")
        Dim compilation3 = state3.Result.Script.GetCompilation()
        compilation3.VerifyDiagnostics()
        Dim c3C3 = DirectCast(lookupMember(compilation3, "Submission#2", "C3"), INamedTypeSymbol)
        Dim c3C1 = c3C3.BaseType
        Dim action As Action = Sub() compilation2.IsSymbolAccessibleWithin(c3C3, c3C1)
        Assert.Throws(Of ArgumentException)(action)
        Assert.True(compilation3.IsSymbolAccessibleWithin(c3C3, c3C1))
    End Sub

    Function lookupType(c As Compilation, name As String) As INamedTypeSymbol
        Return DirectCast(c.GlobalNamespace.GetMembers(name).Single(), INamedTypeSymbol)
    End Function
    Function lookupMember(c As Compilation, typeName As String, memberName As String) As ISymbol
        Return lookupType(c, typeName).GetMembers(memberName).Single()
    End Function

    ''' <summary>
    ''' L4-1: A REPL submission whose first line is a #! shebang is accepted and spins idly
    ''' (no errors, no output, no return value), following C# csi.
    ''' </summary>
    <Fact>
    Public Async Function Shebang_ReplFirstLine_IsAcceptedNoOp() As Task
        Dim script = VisualBasicScript.Create("#!/usr/bin/env csi")
        Assert.DoesNotContain(script.GetCompilation().GetDiagnostics(), Function(d) d.Severity = DiagnosticSeverity.Error)

        Dim state = Await script.RunAsync()
        Assert.Null(state.ReturnValue)
    End Function

    ''' <summary>
    ''' L4-2: The Content API returns the shebang path text as a StringLiteralToken.
    ''' </summary>
    <Fact>
    Public Sub Shebang_ContentApi_ReturnsPathText()
        Dim tree = VisualBasicScript.Create("#!/usr/bin/env" & vbCrLf & "? 1").GetCompilation().SyntaxTrees.First()
        Dim root = DirectCast(tree.GetRoot(), CompilationUnitSyntax)
        Dim shebang = DirectCast(root.GetDirectives().Single(Function(d) d.Kind = SyntaxKind.ShebangDirectiveTrivia), ShebangDirectiveTriviaSyntax)

        Assert.Equal("/usr/bin/env", shebang.Content.ToString())
        Assert.Equal(SyntaxKind.StringLiteralToken, shebang.Content.Kind())
    End Sub

    ''' <summary>
    ''' L4-3: A multi-line submission starting with #! runs normally; the shebang does not
    ''' interfere with submission semantics.
    ''' </summary>
    <Fact>
    Public Async Function Shebang_MultilineSubmission_DoesNotInterfere() As Task
        Dim state = Await VisualBasicScript.RunAsync(
            "#!/usr/bin/env csi" & vbCrLf & "Dim x = 5" & vbCrLf & "? x * 2")
        Assert.Equal(10, state.ReturnValue)
    End Function

    ''' <summary>
    ''' L4-4: WithContent rewrites the path and ToFullString() is consistent, preserving the
    ''' trailing EndOfLineTrivia (the #! line is not merged with the following line).
    ''' </summary>
    <Fact>
    Public Sub Shebang_WithContent_ToFullStringPreservesEndOfLine()
        Dim tree = VisualBasicScript.Create("#!/usr/bin/env" & vbCrLf & "? 1").GetCompilation().SyntaxTrees.First()
        Dim root = DirectCast(tree.GetRoot(), CompilationUnitSyntax)
        Dim shebang = DirectCast(root.GetDirectives().Single(Function(d) d.Kind = SyntaxKind.ShebangDirectiveTrivia), ShebangDirectiveTriviaSyntax)

        Dim rewritten = shebang.WithContent(SyntaxFactory.StringLiteralToken("/new/path", "/new/path"))
        Assert.Equal("#!/new/path" & vbCrLf, rewritten.ToFullString())
    End Sub
End Class
