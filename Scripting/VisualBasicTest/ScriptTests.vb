' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.Text
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

    Private NotInheritable Class InMemorySourceReferenceResolver
        Inherits SourceReferenceResolver

        Private ReadOnly _files As IReadOnlyDictionary(Of String, String)

        Public Sub New(files As IReadOnlyDictionary(Of String, String))
            _files = files
        End Sub

        Public Overrides Function NormalizePath(path As String, baseFilePath As String) As String
            Return If(ResolveReference(path, baseFilePath), path)
        End Function

        Public Overrides Function ResolveReference(path As String, baseFilePath As String) As String
            If _files.ContainsKey(path) Then
                Return path
            End If
            If baseFilePath IsNot Nothing Then
                Dim dir = IO.Path.GetDirectoryName(baseFilePath)
                Dim combined = IO.Path.Combine(If(dir, ""), path)
                If _files.ContainsKey(combined) Then
                    Return combined
                End If
            End If
            Return Nothing
        End Function

        Public Overrides Function OpenRead(resolvedPath As String) As Stream
            Return New MemoryStream(Encoding.UTF8.GetBytes(_files(resolvedPath)))
        End Function

        Public Overrides Function ReadText(resolvedPath As String) As SourceText
            Return SourceText.From(_files(resolvedPath))
        End Function

        Public Overrides Function Equals(other As Object) As Boolean
            Return ReferenceEquals(Me, other)
        End Function

        Public Overrides Function GetHashCode() As Integer
            Return _files.Count
        End Function
    End Class

    Private Shared Function CreateScriptWithLoadDirective(mainCode As String, files As IReadOnlyDictionary(Of String, String)) As Script
        Dim options = s_defaultOptions.WithFilePath("C:\scripts\main.vbx").WithSourceResolver(New InMemorySourceReferenceResolver(files))
        Return VisualBasicScript.Create(mainCode, options)
    End Function

    Private Shared Function GetLineNumber(diagnostic As Diagnostic) As Integer
        Return diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1
    End Function

    <Fact>
    Public Sub TestLoadDirectiveDoesNotShiftDiagnosticSpan()
        ' Issue #01: main.vbx line 3 has Print(undefinedVar); loaded.vbx is 5 lines.
        ' The bug inlined loaded.vbx into main.vbx and reported line 7; it must report line 3.
        Dim files = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From
        {
            {"C:\scripts\loaded.vbx", "Dim a As Integer = 1" & vbCrLf &
                                       "Dim b As Integer = 2" & vbCrLf &
                                       "Function LoadedValue() As Integer" & vbCrLf &
                                       "    Return 42" & vbCrLf &
                                       "End Function"},
            {"C:\scripts\main.vbx", "#Load ""loaded.vbx""" & vbCrLf &
                                     "? LoadedValue()" & vbCrLf &
                                     "Print(undefinedVar)"}
        }

        Dim script = CreateScriptWithLoadDirective(files("C:\scripts\main.vbx"), files)
        Dim diagnostics = script.GetCompilation().GetDiagnostics()
        Dim undefinedVar = diagnostics.Single(Function(d) d.Id = "BC30451" AndAlso d.GetMessage().Contains("undefinedVar"))
        Dim lineSpan = undefinedVar.Location.GetLineSpan()

        Assert.Equal("C:\scripts\main.vbx", lineSpan.Path)
        Assert.Equal(3, lineSpan.StartLinePosition.Line + 1)
    End Sub

    <Fact>
    Public Sub TestLoadedFileDiagnosticsUseRealFileAndLine()
        Dim files = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From
        {
            {"C:\scripts\loaded.vbx", "Dim a As Integer = 1" & vbCrLf &
                                       "Function Bad() As Integer" & vbCrLf &
                                       "    Return undefinedInLoaded" & vbCrLf &
                                       "End Function"},
            {"C:\scripts\main.vbx", "#Load ""loaded.vbx""" & vbCrLf &
                                     "? Bad()"}
        }

        Dim script = CreateScriptWithLoadDirective(files("C:\scripts\main.vbx"), files)
        Dim diagnostics = script.GetCompilation().GetDiagnostics()
        Dim undefinedLoaded = diagnostics.Single(Function(d) d.Id = "BC30451" AndAlso d.GetMessage().Contains("undefinedInLoaded"))
        Dim lineSpan = undefinedLoaded.Location.GetLineSpan()

        Assert.Equal("C:\scripts\loaded.vbx", lineSpan.Path)
        Assert.Equal(3, lineSpan.StartLinePosition.Line + 1)
    End Sub

    <Fact>
    Public Async Function TestMissingLoadDirectiveFileReportsAtLoadLine() As Task
        Dim files = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From
        {
            {"C:\scripts\main.vbx", "#Load ""nope.vbx""" & vbCrLf & "? 1"}
        }

        Dim script = CreateScriptWithLoadDirective(files("C:\scripts\main.vbx"), files)
        Try
            Await script.RunAsync()
            Assert.True(False, "Expected CompilationErrorException")
        Catch ex As CompilationErrorException
            Dim diagnostic = ex.Diagnostics.Single()
            Assert.Equal("C:\scripts\main.vbx", diagnostic.Location.GetLineSpan().Path)
            Assert.Equal(1, GetLineNumber(diagnostic))
        End Try
    End Function

    <Fact>
    Public Async Function TestNestedLoadDirective() As Task
        Dim files = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From
        {
            {"C:\scripts\leaf.vbx", "Function Leaf() As Integer" & vbCrLf & "    Return 7" & vbCrLf & "End Function"},
            {"C:\scripts\mid.vbx", "#Load ""leaf.vbx""" & vbCrLf &
                                   "Function Mid() As Integer" & vbCrLf & "    Return Leaf() + 1" & vbCrLf & "End Function"},
            {"C:\scripts\main.vbx", "#Load ""mid.vbx""" & vbCrLf & "? Mid()"}
        }

        Dim script = CreateScriptWithLoadDirective(files("C:\scripts\main.vbx"), files)
        Dim state = Await script.RunAsync()
        Assert.Equal(8, state.ReturnValue)
    End Function

    <Fact>
    Public Async Function TestLoadDirectiveCycleReportsAtLoadLine() As Task
        Dim files = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From
        {
            {"C:\scripts\main.vbx", "#Load ""mid.vbx""" & vbCrLf & "? 1"},
            {"C:\scripts\mid.vbx", "#Load ""main.vbx""" & vbCrLf &
                                   "Function F() As Integer" & vbCrLf & "    Return 1" & vbCrLf & "End Function"}
        }

        Dim script = CreateScriptWithLoadDirective(files("C:\scripts\main.vbx"), files)
        Try
            Await script.RunAsync()
            Assert.True(False, "Expected CompilationErrorException")
        Catch ex As CompilationErrorException
            Dim diagnostic = ex.Diagnostics.Single()
            ' The cycle is detected at mid.vbx's #Load line.
            Assert.Equal("C:\scripts\mid.vbx", diagnostic.Location.GetLineSpan().Path)
            Assert.Equal(1, GetLineNumber(diagnostic))
        End Try
    End Function

    <Fact>
    Public Async Function TestLoadedFileSeesMetadataReferences() As Task
        ' A #Load'ed file resolves types from the script's references (shared across trees).
        Dim files = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From
        {
            {"C:\scripts\loaded.vbx", "Function AbsValue(x As Integer) As Integer" & vbCrLf & "    Return System.Math.Abs(x)" & vbCrLf & "End Function"},
            {"C:\scripts\main.vbx", "#Load ""loaded.vbx""" & vbCrLf & "? AbsValue(-5)"}
        }

        Dim script = CreateScriptWithLoadDirective(files("C:\scripts\main.vbx"), files)
        Dim state = Await script.RunAsync()
        Assert.Equal(5, state.ReturnValue)
    End Function

    <Fact>
    Public Sub TestLoadDirectiveAfterFirstTokenReportsDiagnostic()
        ' A #Load after code follows the first token and must be reported, not silently ignored.
        Dim files = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From
        {
            {"C:\scripts\loaded.vbx", "Function LoadedValue() As Integer" & vbCrLf & "    Return 42" & vbCrLf & "End Function"},
            {"C:\scripts\main.vbx", "? 1" & vbCrLf & "#Load ""loaded.vbx"""}
        }

        Dim script = CreateScriptWithLoadDirective(files("C:\scripts\main.vbx"), files)
        Dim diagnostics = script.GetCompilation().GetDiagnostics()
        Assert.Contains(diagnostics, Function(d) d.Id = "BC36985")
    End Sub

    <Fact>
    Public Sub TestReferenceDirectiveAfterFirstTokenReportsDiagnostic()
        Dim files = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From
        {
            {"C:\scripts\main.vbx", "? 1" & vbCrLf & "#r ""some.dll"""}
        }

        Dim options = s_defaultOptions.WithFilePath("C:\scripts\main.vbx").WithSourceResolver(New InMemorySourceReferenceResolver(files))
        Dim script = VisualBasicScript.Create(files("C:\scripts\main.vbx"), options)
        Dim diagnostics = script.GetCompilation().GetDiagnostics()
        Assert.Contains(diagnostics, Function(d) d.Id = "BC36984")
    End Sub

    <Fact>
    Public Sub TestLoadDirectiveAtFirstTokenHasNoFollowsTokenDiagnostic()
        ' A first-line #Load is legal: no follows-token diagnostic.
        Dim files = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From
        {
            {"C:\scripts\loaded.vbx", "Function LoadedValue() As Integer" & vbCrLf & "    Return 42" & vbCrLf & "End Function"},
            {"C:\scripts\main.vbx", "#Load ""loaded.vbx""" & vbCrLf & "? LoadedValue()"}
        }

        Dim script = CreateScriptWithLoadDirective(files("C:\scripts\main.vbx"), files)
        Dim diagnostics = script.GetCompilation().GetDiagnostics()
        Assert.DoesNotContain(diagnostics, Function(d) d.Id = "BC36985")
    End Sub

    <Fact>
    Public Async Function TestLoadDirectiveAtFirstTokenOfLoadedFileIsLegal() As Task
        ' A #Load on the first line of a loaded file (per-tree position 0) is legal.
        Dim files = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From
        {
            {"C:\scripts\leaf.vbx", "Function Leaf() As Integer" & vbCrLf & "    Return 5" & vbCrLf & "End Function"},
            {"C:\scripts\loaded.vbx", "#Load ""leaf.vbx""" & vbCrLf &
                                     "Function L() As Integer" & vbCrLf & "    Return Leaf()" & vbCrLf & "End Function"},
            {"C:\scripts\main.vbx", "#Load ""loaded.vbx""" & vbCrLf & "? L()"}
        }

        Dim script = CreateScriptWithLoadDirective(files("C:\scripts\main.vbx"), files)
        Dim state = Await script.RunAsync()
        Assert.Equal(5, state.ReturnValue)
    End Function

    ' TODO: port C# tests
End Class
