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

    Public Class EventGlobals
        Public Count As Integer

        Public Event Changed As EventHandler

        Public Sub Handler(sender As Object, e As EventArgs)
            Count += 10
        End Sub

        Public Sub RaiseChanged()
            RaiseEvent Changed(Me, EventArgs.Empty)
        End Sub
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
    Public Async Function TestCreateTypedScript() As Task
        Dim script = VisualBasicScript.Create(Of Integer)("Return 1 + 2", s_defaultOptions)

        Assert.Equal(3, Await script.EvaluateAsync())
    End Function

    <Fact>
    Public Async Function TestCreateScriptDelegate() As Task
        Dim script = VisualBasicScript.Create("? 1 + 2", s_defaultOptions)
        Dim runner = script.CreateDelegate()

        Assert.Equal(3, Await runner())
        Await Assert.ThrowsAsync(Of ArgumentException)(
            Async Function()
                Await runner(New Object())
            End Function)
    End Function

    <Fact>
    Public Async Function TestCreateTypedScriptDelegateWithGlobals() As Task
        Dim script = VisualBasicScript.Create(Of Integer)("Return Add(5)", s_defaultOptions, globalsType:=GetType(Globals))
        Dim runner = script.CreateDelegate()

        Assert.Equal(10, Await runner(New Globals()))
    End Function

    <Fact>
    Public Async Function TestScriptVariableSetValue() As Task
        Dim state = Await VisualBasicScript.RunAsync("Dim x = 1", s_defaultOptions)
        Dim variable = state.GetVariable("x")

        variable.Value = 2
        Assert.Equal(2, variable.Value)

        Dim rerunState = Await state.Script.RunAsync()
        Assert.Equal(1, rerunState.GetVariable("x").Value)

        Dim continuedState = Await state.ContinueWithAsync("? x")
        Assert.Equal(2, continuedState.ReturnValue)
    End Function

    <Fact>
    Public Async Function TestScriptVariableSetValueTypeMismatch() As Task
        Dim state = Await VisualBasicScript.RunAsync("Dim x As Integer = 1", s_defaultOptions)

        Assert.Throws(Of ArgumentException)(Sub() state.GetVariable("x").Value = "str")
    End Function

    <Fact>
    Public Async Function TestRunScriptWithTypedReturnType() As Task
        Dim script = VisualBasicScript.Create(Of Integer)("Return 7", s_defaultOptions)

        Dim state = Await script.RunAsync()

        Assert.Equal(7, state.ReturnValue)
    End Function

    <Fact>
    Public Async Function TestTypedScriptIgnoresTrailingExpression() As Task
        ' A typed script follows the VB Function Main semantics: the result comes only from an explicit
        ' Return statement. The trailing expression (even an unconvertible one like a Guid) is ignored,
        ' so the result defaults to 0 instead of reporting a conversion error.
        Dim state = Await VisualBasicScript.RunAsync(Of Integer)("? New System.Guid()", s_defaultOptions)

        Assert.Equal(0, state.ReturnValue)
    End Function

    <Fact>
    Public Async Function TestTypedScriptBareReturnIsZero() As Task
        ' Function Main semantics: a bare Return in a typed script returns the default value (0).
        Dim state = Await VisualBasicScript.RunAsync(Of Integer)("Return", s_defaultOptions)

        Assert.Equal(0, state.ReturnValue)
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
    Public Async Function TestTopLevelReturnPrecedesTrailingExpression() As Task
        Dim state = Await VisualBasicScript.RunAsync("Return 7
? 9", s_defaultOptions)

        Assert.Equal(7, state.ReturnValue)
    End Function

    <Fact>
    Public Async Function TestReturnValueInLoadedFile() As Task
        Dim directory = Path.Combine(AppContext.BaseDirectory, "TestTemp", Guid.NewGuid().ToString("N"))
        System.IO.Directory.CreateDirectory(directory)
        Try
            Dim mainPath = Path.Combine(directory, "main.vbx")
            File.WriteAllText(Path.Combine(directory, "loaded.vbx"), "Return 17")

            Dim options = s_defaultOptions.WithFilePath(mainPath)
            Dim state = Await VisualBasicScript.RunAsync("#Load ""loaded.vbx""", options)

            Assert.Equal(17, state.ReturnValue)
        Finally
            System.IO.Directory.Delete(directory, recursive:=True)
        End Try
    End Function

    <Fact>
    Public Async Function TestTopLevelExecutableStatements() As Task
        Dim state = Await VisualBasicScript.RunAsync("
Dim total = 1
Const stepValue As Integer = 2
total = total + stepValue
total += 3
Call System.Console.Write("""")
System.Math.Abs(-4)
If total = 6 Then
    total += 10
Else
    total = -1
End If
Select Case total
    Case 16
        total += 20
    Case Else
        total = -2
End Select
For i = 1 To 3
    total += i
Next
Dim j = 0
While j < 2
    total += j
    j += 1
End While
Do
    j += 1
    total += j
Loop Until j = 4
For Each item In New Integer() {1, 2}
    total += item
Next
Using reader As New System.IO.StringReader(""xy"")
    total += reader.ReadToEnd().Length
End Using
Dim builder = New System.Text.StringBuilder()
With builder
    .Append(""abc"")
    total += .Length
End With
Dim gate = New Object()
SyncLock gate
    total += 7
End SyncLock
Try
    Throw New System.InvalidOperationException(""boom"")
Catch ex As System.InvalidOperationException
    total += 11
Finally
    total += 13
End Try
Return total", s_defaultOptions)

        Assert.Equal(89, state.ReturnValue)
    End Function

    <Fact>
    Public Sub TestTopLevelGoToLabelStatementCompiles()
        Dim diagnostics = VisualBasicScript.Create("
Dim total = 1
GoTo done
total = -1000
done:
Return total", s_defaultOptions).GetCompilation().GetDiagnostics()

        Assert.DoesNotContain(diagnostics, Function(d) d.Severity = DiagnosticSeverity.Error)
    End Sub

    <Fact>
    Public Async Function TestTopLevelThrowStatement() As Task
        Await Assert.ThrowsAsync(Of InvalidOperationException)(
            Async Function()
                Await VisualBasicScript.RunAsync("Throw New System.InvalidOperationException(""boom"")", s_defaultOptions)
            End Function)
    End Function

    <Fact>
    Public Sub TestTopLevelOnErrorStatementReportsUnsupportedDiagnostic()
        AssertDiagnosticsContainAny("On Error Resume Next
Return 1", "BC30024", "BC30188", "BC30205", "BC36956")
    End Sub

    <Fact>
    Public Async Function TestTopLevelAddHandlerWithLambda() As Task
        Dim state = Await VisualBasicScript.RunAsync("
Dim callback As System.EventHandler = Sub(sender As Object, e As System.EventArgs)
                                          Count += 1
                                      End Sub
AddHandler Changed, callback
RaiseChanged()
Return Count", s_defaultOptions, globals:=New EventGlobals())
        Assert.Equal(1, state.ReturnValue)
    End Function

    <Fact>
    Public Async Function TestTopLevelRemoveHandler() As Task
        Dim state = Await VisualBasicScript.RunAsync("
AddHandler Changed, AddressOf Handler
RemoveHandler Changed, AddressOf Handler
RaiseChanged()
Return Count", s_defaultOptions, globals:=New EventGlobals())
        Assert.Equal(0, state.ReturnValue)
    End Function

    <Fact>
    Public Async Function TestTopLevelAddHandlerWithPreviousSubmissionHandler() As Task
        Dim state = Await VisualBasicScript.
            Create("Dim callback As System.EventHandler = AddressOf Handler", s_defaultOptions, globalsType:=GetType(EventGlobals)).
            ContinueWith("AddHandler Changed, callback
RaiseChanged()
Return Count").
            RunAsync(New EventGlobals())
        Assert.Equal(10, state.ReturnValue)
    End Function

    <Fact>
    Public Sub TestTopLevelRaiseEventStatementReportsUnsupportedDiagnostic()
        AssertDiagnosticsContainAnyWithGlobalsType("RaiseEvent Changed(Nothing, System.EventArgs.Empty)", GetType(EventGlobals), "BC30024", "BC30188", "BC30205")
    End Sub

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
    Public Async Function TestTopLevelBareAwaitStatement() As Task
        Dim options = s_defaultOptions.
            AddReferences(GetType(Task).Assembly).
            AddImports("System.Threading.Tasks")

        ' A bare "Await <expr>" as a standalone top-level statement used to fail to parse.
        Dim state = Await VisualBasicScript.RunAsync("Await Task.FromResult(11)", options)

        Assert.Equal(11, state.ReturnValue)
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

    Private Shared Sub AssertDiagnosticsContainAny(code As String, ParamArray expectedIds() As String)
        AssertDiagnosticsContainAnyCore(code, globalsType:=Nothing, expectedIds:=expectedIds)
    End Sub

    Private Shared Sub AssertDiagnosticsContainAnyWithGlobalsType(code As String, globalsType As Type, ParamArray expectedIds() As String)
        AssertDiagnosticsContainAnyCore(code, globalsType, expectedIds)
    End Sub

    Private Shared Sub AssertDiagnosticsContainAnyCore(code As String, globalsType As Type, expectedIds() As String)
        Dim diagnostics = VisualBasicScript.Create(code, s_defaultOptions, globalsType:=globalsType).GetCompilation().GetDiagnostics()
        AssertDiagnosticsContainAny(diagnostics, expectedIds)
    End Sub

    Private Shared Sub AssertDiagnosticsContainAny(diagnostics As IEnumerable(Of Diagnostic), ParamArray expectedIds() As String)
        Assert.Contains(diagnostics, Function(d) expectedIds.Contains(d.Id))
    End Sub

    ' TODO: port C# tests
End Class
