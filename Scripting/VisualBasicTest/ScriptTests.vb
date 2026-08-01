' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.IO
Imports System.Text
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis.Scripting
Imports Xunit

Public Class ScriptTests

    Public Class Globals
        Public Field As Integer = 2
        Public Property Value As Integer = 3

        Public Function Add(value As Integer) As Integer
            Return Field + Me.Value + value
        End Function
    End Class

    Public Class OtherGlobals
        Public Value As Integer = 10
    End Class

    Public Class DerivedGlobals
        Inherits Globals
    End Class

    ''' <summary>
    ''' Need to create a <see cref="PortableExecutableReference"/> without a file path here. Scripting
    ''' will attempt to validate file paths and one does not exist for this reference as it's an in
    ''' memory item.
    ''' </summary>
    Private Shared ReadOnly s_msvbReference As PortableExecutableReference = AssemblyMetadata.CreateFromImage(
        File.ReadAllBytes(GetType(Strings).Assembly.Location)).GetReference()

    ' It shouldn't be necessary to include VB runtime assembly
    ' explicitly in VisualBasicScript.Create.
    Private Shared ReadOnly s_defaultOptions As ScriptOptions = ScriptOptions.Default.AddReferences(s_msvbReference)

    <Fact>
    Public Sub TestCreateScript()
        Dim script = VisualBasicScript.Create("? 1 + 2")
        Assert.Equal("? 1 + 2", script.Code)
    End Sub

    <Fact>
    Public Sub TestCreateScriptFromStream()
        Using stream = New MemoryStream(Encoding.UTF8.GetBytes("? 2 + 3"))
            Dim script = VisualBasicScript.Create(stream)
            Assert.Equal("? 2 + 3", script.Code)
        End Using
    End Sub

    <Fact>
    Public Async Function TestRunNullScript() As Task
        Dim state = Await VisualBasicScript.RunAsync(code:=DirectCast(Nothing, String), options:=s_defaultOptions)
        Assert.Equal("", state.Script.Code)
        Assert.Null(state.ReturnValue)
    End Function

    <Fact>
    Public Sub TestEvalScript()
        Dim value = VisualBasicScript.EvaluateAsync("? 1 + 2", s_defaultOptions)
        Assert.Equal(3, value.Result)
    End Sub

    <Fact>
    Public Async Function TestRunScript() As Task
        Dim state = Await VisualBasicScript.RunAsync("? 1 + 2", s_defaultOptions)
        Assert.Equal(3, state.ReturnValue)
    End Function

    <Fact>
    Public Async Function TestCreateAndRunScript() As Task
        Dim script = VisualBasicScript.Create("? 1 + 2", s_defaultOptions)
        Dim state = Await script.RunAsync()
        Assert.Same(script, state.Script)
        Assert.Equal(3, state.ReturnValue)
    End Function

    <Fact>
    Public Async Function TestRunScriptWithSpecifiedReturnType() As Task
        Dim state = Await VisualBasicScript.RunAsync("? 1 + 2", s_defaultOptions)
        Assert.Equal(3, state.ReturnValue)
    End Function

    <Fact>
    Public Sub TestGetCompilation()
        Dim script = VisualBasicScript.Create("? 1 + 2")
        Dim compilation = script.GetCompilation()
        Assert.Equal(script.Code, compilation.SyntaxTrees.First().GetText().ToString())
    End Sub

    <Fact>
    Public Async Function TestRunVoidScript() As Task
        Dim state = Await VisualBasicScript.RunAsync("System.Console.WriteLine(0)", s_defaultOptions)
        Assert.Null(state.ReturnValue)
    End Function

    <Fact>
    Public Async Function TestExpressionStatementReturnValue() As Task
        Dim state = Await VisualBasicScript.RunAsync("System.Math.Abs(-4)", s_defaultOptions)
        Assert.Equal(4, state.ReturnValue)
    End Function

    <Fact>
    Public Async Function TestCallStatementReturnValue() As Task
        Dim state = Await VisualBasicScript.RunAsync("Call System.Math.Abs(-4)", s_defaultOptions)
        Assert.Equal(4, state.ReturnValue)
    End Function

    <Fact>
    Public Async Function TestTopLevelReturnValue() As Task
        Dim state = Await VisualBasicScript.RunAsync("Return 7", s_defaultOptions)
        Assert.Equal(7, state.ReturnValue)
    End Function

    <Fact>
    Public Async Function TestTopLevelAwaitReturnValue() As Task
        Dim options = s_defaultOptions.
            AddReferences(GetType(Task).Assembly).
            AddImports("System.Threading.Tasks")

        Dim state = Await VisualBasicScript.RunAsync("? Await Task.FromResult(11)", options)

        Assert.Equal(11, state.ReturnValue)
    End Function

    <Fact>
    Public Async Function TestTopLevelAwaitInStatement() As Task
        Dim options = s_defaultOptions.
            AddReferences(GetType(Task).Assembly).
            AddImports("System.Threading.Tasks")

        Dim state = Await VisualBasicScript.RunAsync("Dim value = Await Task.FromResult(13)
? value", options)

        Assert.Equal(13, state.ReturnValue)
    End Function

    <Fact>
    Public Async Function TestGlobalsMembers() As Task
        Dim state = Await VisualBasicScript.RunAsync("? Add(5)", globals:=New Globals())
        Assert.Equal(10, state.ReturnValue)
    End Function

    <Fact>
    Public Async Function TestGlobalsAcrossSubmissions() As Task
        Dim state = Await VisualBasicScript.
            RunAsync("Dim local = Value", globals:=New Globals()).
            ContinueWith("? Add(local)")

        Assert.Equal(8, state.ReturnValue)
    End Function

    <Fact>
    Public Async Function TestGlobalsTypeMismatch() As Task
        Dim script = VisualBasicScript.Create("? Value", globalsType:=GetType(Globals))

        Await Assert.ThrowsAsync(Of ArgumentException)(
            Async Function()
                Await script.RunAsync(New OtherGlobals())
            End Function)
    End Function

    <Fact>
    Public Async Function TestRunScriptWithExplicitGlobalsType() As Task
        Dim script = VisualBasicScript.Create(
            code:="? Add(5)",
            options:=s_defaultOptions,
            globalsType:=GetType(Globals))

        Dim state = Await script.RunAsync(New DerivedGlobals())

        Assert.Equal(10, state.ReturnValue)
    End Function

    <Fact>
    Public Async Function TestEvaluateScriptFromStreamWithExplicitGlobalsType() As Task
        Using stream = New MemoryStream(Encoding.UTF8.GetBytes("? Add(5)"))
            Dim value = Await VisualBasicScript.EvaluateAsync(
                code:=stream,
                options:=s_defaultOptions,
                globals:=New DerivedGlobals(),
                globalsType:=GetType(Globals))

            Assert.Equal(10, value)
        End Using
    End Function

    <Fact>
    Public Sub TestDefaultNamespaces()
        ' If this ever changes, it is important to ensure that the 
        ' IDE is also updated with the same default namespaces.
        Assert.Empty(ScriptOptions.Default.Imports)
    End Sub

    ' TODO: port C# tests
End Class
