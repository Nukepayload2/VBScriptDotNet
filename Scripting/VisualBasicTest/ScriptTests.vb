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
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
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

    ''' <summary>
    ''' Path-SPELLING coverage for #Load de-duplication. The shared InMemorySourceReferenceResolver above
    ''' keys its lookup by the literal text of the directive, so ".\lib.vbx" and "sub\..\lib.vbx" do not
    ''' even resolve there: they report BC2001 instead of ever reaching the de-duplication set (P-020).
    ''' <para>
    ''' The production SourceFileResolver has no such gap -- NormalizePath and ResolveReference both bottom
    ''' out in FileUtilities.TryNormalizeAbsolutePath, which is Path.GetFullPath, so "." and ".." are folded
    ''' onto one canonical absolute path BEFORE the set is consulted. This private subclass reproduces exactly
    ''' that one folding step (plus production's "unknown file yields Nothing" rule) so the spelling dimension
    ''' has a unit test of its own rather than resting only on the vbi.exe end-to-end readings recorded in
    ''' tmp\vortex-logs\load-directive-dedup\7-implementer-gap-closure.md.
    ''' </para>
    ''' <para>
    ''' Deliberately NOT a change to the shared resolver: every existing #Load test keeps the
    ''' literal-spelling resolver it was written against.
    ''' </para>
    ''' </summary>
    Private NotInheritable Class PathNormalizingInMemorySourceReferenceResolver
        Inherits SourceReferenceResolver

        Private ReadOnly _files As IReadOnlyDictionary(Of String, String)

        Public Sub New(files As IReadOnlyDictionary(Of String, String))
            Dim canonical = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            For Each entry In files
                canonical(IO.Path.GetFullPath(entry.Key)) = entry.Value
            Next
            _files = canonical
        End Sub

        Public Overrides Function NormalizePath(path As String, baseFilePath As String) As String
            Return TryResolve(path, baseFilePath)
        End Function

        Public Overrides Function ResolveReference(path As String, baseFilePath As String) As String
            Return TryResolve(path, baseFilePath)
        End Function

        ''' Mirrors FileUtilities.TryNormalizeAbsolutePath: fold the path lexically, then report it only
        ''' if it names a file this resolver actually knows (production returns Nothing otherwise, which is
        ''' what makes the main-path seeding a no-op for an unknown entry file).
        Private Function TryResolve(path As String, baseFilePath As String) As String
            Dim candidate = path
            If Not IO.Path.IsPathRooted(candidate) AndAlso baseFilePath IsNot Nothing Then
                Dim dir = IO.Path.GetDirectoryName(baseFilePath)
                candidate = IO.Path.Combine(If(dir, String.Empty), candidate)
            End If

            Dim normalized = IO.Path.GetFullPath(candidate)
            Return If(_files.ContainsKey(normalized), normalized, Nothing)
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

    Private Shared Function CreateScriptWithNormalizingLoadDirective(mainCode As String, files As IReadOnlyDictionary(Of String, String)) As Script
        Dim options = s_defaultOptions.WithFilePath("C:\scripts\main.vbx").WithSourceResolver(New PathNormalizingInMemorySourceReferenceResolver(files))
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

    ''' <summary>
    ''' Once semantics, re-decided 2026-09-28. A #Load chain that loops back is NOT an error: the file has
    ''' already been expanded in this compilation, so it is skipped silently. main.vbx #Load "mid.vbx" and
    ''' mid.vbx #Load "main.vbx" therefore expands each file exactly once and compiles clean.
    ''' <para>
    ''' This test was originally <c>TestLoadDirectiveCycleReportsAtLoadLine</c> and asserted the opposite
    ''' shape -- one BC2001 ERR_FileNotFound at mid.vbx's #Load line. That assertion encoded the misleading
    ''' diagnostic of issue 34-B ("file not found" for a file that demonstrably exists and that the layer
    ''' above had just opened). The cycle guard is now gone by design rather than re-coded, so the test is
    ''' rewritten to the new conclusion rather than deleted (decisions.md D7, test recycling).
    ''' </para>
    ''' <para>
    ''' The observable is NOT "it did not throw": the tree count pins "each file expanded exactly once"
    ''' (mid, main -- the loop back to main.vbx is a skip), and the per-file print count pins that each
    ''' file's top-level code ran exactly once.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Async Function TestLoadDirectiveCycleExpandsEachFileOnce() As Task
        Dim files = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From
        {
            {"C:\scripts\main.vbx", "#Load ""mid.vbx""" & vbCrLf &
                                     "Imports System" & vbCrLf &
                                     "Console.WriteLine(""MAIN-ran"")"},
            {"C:\scripts\mid.vbx", "#Load ""main.vbx""" & vbCrLf &
                                    "Imports System" & vbCrLf &
                                    "Console.WriteLine(""MID-ran"")"}
        }

        Dim script = CreateScriptWithLoadDirective(files("C:\scripts\main.vbx"), files)

        Dim errorIds = script.GetCompilation().GetDiagnostics().
            Where(Function(d) d.Severity = DiagnosticSeverity.Error).
            Select(Function(d) d.Id).ToArray()
        Assert.Empty(errorIds)

        ' mid, main -- main.vbx is reachable a second time through mid's #Load, and is skipped.
        Assert.Equal(2, script.GetCompilation().SyntaxTrees.Count())

        Dim output = Await RunAndCaptureConsole(script)
        Assert.Equal(1, CountMarkerLines(output, "MID-ran"))
        Assert.Equal(1, CountMarkerLines(output, "MAIN-ran"))
    End Function

    ''' <summary>
    ''' A file that #Loads itself: once semantics means the self-load is a repeat, so it is skipped and the
    ''' script compiles clean with only its own tree. This is the shape C# gets wrong (it never seeds the
    ''' entry file's path, so the self-load escapes its own check and the body runs twice).
    ''' </summary>
    <Fact>
    Public Async Function TestSelfLoadDirectiveExpandsEntryFileOnce() As Task
        Dim files = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From
        {
            {"C:\scripts\main.vbx", "#Load ""main.vbx""" & vbCrLf &
                                     "Imports System" & vbCrLf &
                                     "Console.WriteLine(""SELF-ran"")"}
        }

        Dim script = CreateScriptWithLoadDirective(files("C:\scripts\main.vbx"), files)

        Dim errorIds = script.GetCompilation().GetDiagnostics().
            Where(Function(d) d.Severity = DiagnosticSeverity.Error).
            Select(Function(d) d.Id).ToArray()
        Assert.Empty(errorIds)

        ' Just main.vbx itself -- the self-load expanded nothing.
        Assert.Equal(1, script.GetCompilation().SyntaxTrees.Count())

        Assert.Equal(1, CountMarkerLines(Await RunAndCaptureConsole(script), "SELF-ran"))
    End Function

    ''' <summary>
    ''' Runs <paramref name="script"/> with the real System.Console redirected to a buffer and returns what
    ''' it wrote. Used to observe a top-level print side effect (a file that only prints has no other
    ''' observable effect). System.Console is process-global; this is safe here because no other test in
    ''' this assembly reads the real console -- the runner tests capture through their own TestConsoleIO
    ''' StringWriter, not through Console.Out.
    ''' </summary>
    Private Shared Async Function RunAndCaptureConsole(script As Script) As Task(Of String)
        Dim buffer As New StringWriter()
        Dim originalOut = System.Console.Out
        Try
            System.Console.SetOut(buffer)
            Await script.RunAsync()
        Finally
            System.Console.SetOut(originalOut)
        End Try

        Return buffer.ToString()
    End Function

    ''' <summary>
    ''' Counts how many lines of <paramref name="output"/> are exactly <paramref name="marker"/>. Counting
    ''' occurrences (not just "contains") is the point: a repeat expansion shows up as 2, and the marker is
    ''' unique per file so a neighbouring file's print cannot be mistaken for it.
    ''' </summary>
    Private Shared Function CountMarkerLines(output As String, marker As String) As Integer
        Dim lines = output.Split(New String() {vbCrLf, vbLf}, System.StringSplitOptions.None)
        Return lines.Count(Function(line) line.Trim() = marker)
    End Function

    ''' <summary>
    ''' 1-1 (issue 34): a.vbx and b.vbx both #Load lib.vbx, which declares a variable. lib.vbx must be
    ''' spliced in ONCE. Before the fix it was expanded twice, so "z" was declared twice and the script
    ''' failed with BC30260 + BC31429 -- both reported against the library, not against the script that
    ''' caused it. Asserts the exact error-ID set is empty and the exact tree count, not just "it ran".
    ''' </summary>
    <Fact>
    Public Async Function TestSharedLibraryLoadedByTwoFilesIsExpandedOnce() As Task
        Dim files = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From
        {
            {"C:\scripts\lib.vbx", "Dim z As Integer = 9"},
            {"C:\scripts\a.vbx", "#Load ""lib.vbx""" & vbCrLf &
                                  "Imports System" & vbCrLf &
                                  "Console.WriteLine(""A-ran"")"},
            {"C:\scripts\b.vbx", "#Load ""lib.vbx""" & vbCrLf &
                                  "Imports System" & vbCrLf &
                                  "Console.WriteLine(""B-ran"")"},
            {"C:\scripts\main.vbx", "#Load ""a.vbx""" & vbCrLf &
                                     "#Load ""b.vbx""" & vbCrLf &
                                     "? z"}
        }

        Dim script = CreateScriptWithLoadDirective(files("C:\scripts\main.vbx"), files)

        ' The exact set of error IDs must be empty: neither BC30260 (redeclaration) nor BC31429
        ' (ambiguous name) may show up.
        Dim errorIds = script.GetCompilation().GetDiagnostics().
            Where(Function(d) d.Severity = DiagnosticSeverity.Error).
            Select(Function(d) d.Id).ToArray()
        Assert.Empty(errorIds)

        ' lib, a, b, main -- lib exactly once. Before the fix this list was lib, a, lib, b, main.
        Assert.Equal(4, script.GetCompilation().SyntaxTrees.Count())

        Dim state = Await script.RunAsync()
        Assert.Equal(9, state.ReturnValue)
    End Function

    ''' <summary>
    ''' 1-2 (issue 34): the same file #Load'ed twice in one main file. Its code must run ONCE. The
    ''' accumulator is the observable, not the absence of an error: part.vbx bumps "hits" once as a
    ''' top-level side effect, and the trailing expression reads that submission field back, so a second
    ''' expansion would read 2 (and would additionally collide on the duplicate "Dim").
    ''' <para>
    ''' The second #Load is deliberately spelled "Part.vbx" rather than "part.vbx", so this one test also
    ''' pins the SET's COMPARER. The resolved paths really do differ -- the in-memory resolver returns
    ''' "C:\scripts\part.vbx" and "C:\scripts\Part.vbx" verbatim, and the production resolver's
    ''' Path.GetFullPath collapses "." and ".." but deliberately preserves case. Case-insensitive
    ''' de-duplication therefore rests entirely on StringComparer.OrdinalIgnoreCase: swap it for Ordinal
    ''' and this test goes red with 3 trees instead of 2.
    ''' </para>
    ''' <para>
    ''' Scope note (2026-09-28): path SPELLING splits across two tests. The CASE dimension is pinned right
    ''' here, because the shared in-memory resolver returns the spelled text verbatim. The "."/".." dimension
    ''' cannot be expressed through that resolver (Path.Combine does not fold "..", so the spelling would
    ''' report BC2001 instead of dedup) and is pinned separately by
    ''' TestLoadWithDifferentPathSpellingsExpandsOnce, which uses a resolver that folds the way the production
    ''' SourceFileResolver does. See tmp\vortex-logs\load-directive-dedup\7-implementer-gap-closure.md.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Async Function TestFileLoadedTwiceRunsItsCodeOnce() As Task
        Dim files = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From
        {
            {"C:\scripts\part.vbx", "Dim q As Integer = 2" & vbCrLf &
                                     "Dim hits As Integer = 0" & vbCrLf &
                                     "Sub Bump()" & vbCrLf &
                                     "    hits = hits + 1" & vbCrLf &
                                     "End Sub" & vbCrLf &
                                     "Bump()"},
            {"C:\scripts\main.vbx", "#Load ""part.vbx""" & vbCrLf &
                                     "#Load ""Part.vbx""" & vbCrLf &
                                     "? hits"}
        }

        Dim script = CreateScriptWithLoadDirective(files("C:\scripts\main.vbx"), files)

        Dim errorIds = script.GetCompilation().GetDiagnostics().
            Where(Function(d) d.Severity = DiagnosticSeverity.Error).
            Select(Function(d) d.Id).ToArray()
        Assert.Empty(errorIds)

        ' part, main -- part exactly once. Before the fix this list was part, part, main.
        Assert.Equal(2, script.GetCompilation().SyntaxTrees.Count())

        ' part's only top-level statement is Bump(), so exactly one expansion means exactly one bump.
        Dim state = Await script.RunAsync()
        Assert.Equal(1, state.ReturnValue)
    End Function

    ''' <summary>
    ''' 1-3 (issue 34): the shared library only prints and declares nothing, so a duplicate expansion
    ''' produced no diagnostic at all -- just the line twice (measured before the fix on
    ''' tmp\probes-cyc2\dup-main.vbx). The print must appear exactly once.
    ''' </summary>
    <Fact>
    Public Async Function TestPrintOnlyLibraryLoadedByTwoFilesPrintsOnce() As Task
        Dim files = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From
        {
            {"C:\scripts\lib.vbx", "Imports System" & vbCrLf &
                                     "Console.WriteLine(""LIB-ran"")"},
            {"C:\scripts\a.vbx", "#Load ""lib.vbx""" & vbCrLf &
                                  "Imports System" & vbCrLf &
                                  "Console.WriteLine(""A-ran"")"},
            {"C:\scripts\b.vbx", "#Load ""lib.vbx""" & vbCrLf &
                                  "Imports System" & vbCrLf &
                                  "Console.WriteLine(""B-ran"")"},
            {"C:\scripts\main.vbx", "#Load ""a.vbx""" & vbCrLf &
                                     "#Load ""b.vbx""" & vbCrLf &
                                     "Imports System" & vbCrLf &
                                     "Console.WriteLine(""MAIN-ran"")"}
        }

        Dim script = CreateScriptWithLoadDirective(files("C:\scripts\main.vbx"), files)

        Dim errorIds = script.GetCompilation().GetDiagnostics().
            Where(Function(d) d.Severity = DiagnosticSeverity.Error).
            Select(Function(d) d.Id).ToArray()
        Assert.Empty(errorIds)

        Assert.Equal(1, CountMarkerLines(Await RunAndCaptureConsole(script), "LIB-ran"))
    End Function

    ''' <summary>
    ''' 2-3: de-duplication is per compilation, not per process. Two unrelated scripts that each #Load the
    ''' same file must both load it -- if the "already expanded" set were shared across submissions, the
    ''' second script would silently lose the library. This is the boundary the per-submission set has to hold.
    ''' </summary>
    <Fact>
    Public Async Function TestLoadDedupIsPerCompilationNotPerProcess() As Task
        Dim files = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From
        {
            {"C:\scripts\lib.vbx", "Dim z As Integer = 9"},
            {"C:\scripts\a.vbx", "#Load ""lib.vbx""" & vbCrLf &
                                  "Imports System" & vbCrLf &
                                  "Console.WriteLine(""A-ran"")"},
            {"C:\scripts\b.vbx", "#Load ""lib.vbx""" & vbCrLf &
                                  "Imports System" & vbCrLf &
                                  "Console.WriteLine(""B-ran"")"},
            {"C:\scripts\main.vbx", "#Load ""a.vbx""" & vbCrLf &
                                     "#Load ""b.vbx""" & vbCrLf &
                                     "? z"}
        }

        Dim first = CreateScriptWithLoadDirective(files("C:\scripts\main.vbx"), files)
        Assert.Equal(4, first.GetCompilation().SyntaxTrees.Count())

        ' Run the first script to completion BEFORE the second one is created: a set shared across
        ' submissions would still hold lib.vbx at that point and the second script would lose it.
        Assert.Equal(9, (Await first.RunAsync()).ReturnValue)

        Dim second = CreateScriptWithLoadDirective(files("C:\scripts\main.vbx"), files)
        Assert.Equal(4, second.GetCompilation().SyntaxTrees.Count())
        Assert.Equal(9, (Await second.RunAsync()).ReturnValue)
    End Function

    ''' <summary>
    ''' Path-SPELLING dimension of #Load de-duplication, added 2026-09-28 to close the coverage gap named in
    ''' pitfalls P-020: the shared in-memory resolver cannot resolve ".\lib.vbx" or "sub\..\lib.vbx" at
    ''' all, so before this test no unit test could even express the shape "the same file, spelled
    ''' differently, reached twice".
    ''' <para>
    ''' One library reached through four spellings in one main script -- plain, ".\", "./", and a "sub\.."
    ''' round trip -- must still be expanded ONCE. Two observables, both non-trivial:
    ''' the error set (a second expansion collides on the duplicate "Dim z" with BC30260) and the tree
    ''' count (lib + main = 2). The accumulator makes it three: a second expansion also bumps "hits" twice.
    ''' </para>
    ''' <para>
    ''' What this test pins, stated precisely so it is not over-claimed: it pins that the de-duplication key
    ''' is the RESOLVER'S RESOLVED PATH and not the directive's literal text (VisualBasicScriptCompiler.vb
    ''' CollectLoadTrees, "expandedFiles.Add(resolvedPath)"). It does NOT pin that the production resolver
    ''' folds "."/".." -- that half rests on Path.GetFullPath semantics verified end-to-end over real files
    ''' with vbi.exe (see 7-implementer-gap-closure.md and verifier findings B.2/B.3).
    ''' </para>
    ''' </summary>
    <Fact>
    Public Async Function TestLoadWithDifferentPathSpellingsExpandsOnce() As Task
        Dim files = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From
        {
            {"C:\scripts\lib.vbx", "Dim z As Integer = 4" & vbCrLf &
                                     "Dim hits As Integer = 0" & vbCrLf &
                                     "Sub Bump()" & vbCrLf &
                                     "    hits = hits + 1" & vbCrLf &
                                     "End Sub" & vbCrLf &
                                     "Bump()"},
            {"C:\scripts\main.vbx", "#Load ""lib.vbx""" & vbCrLf &
                                     "#Load "".\lib.vbx""" & vbCrLf &
                                     "#Load ""./lib.vbx""" & vbCrLf &
                                     "#Load ""sub\..\lib.vbx""" & vbCrLf &
                                     "#Load ""sub/./../lib.vbx""" & vbCrLf &
                                     "? hits"}
        }

        Dim script = CreateScriptWithNormalizingLoadDirective(files("C:\scripts\main.vbx"), files)

        Dim errorIds = script.GetCompilation().GetDiagnostics().
            Where(Function(d) d.Severity = DiagnosticSeverity.Error).
            Select(Function(d) d.Id).ToArray()
        Assert.Empty(errorIds)

        ' lib, main -- lib exactly once. A per-spelling key would make this 6.
        Assert.Equal(2, script.GetCompilation().SyntaxTrees.Count())

        ' lib's only top-level statement is Bump(), so exactly one expansion means exactly one bump.
        Assert.Equal(1, (Await script.RunAsync()).ReturnValue)
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
        Assert.Contains(diagnostics, Function(d) d.Id = "BC37002")
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
        Assert.Contains(diagnostics, Function(d) d.Id = "BC36959")
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
        Assert.DoesNotContain(diagnostics, Function(d) d.Id = "BC37002")
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

    ''' <summary>
    ''' L3-1: A .vbx first-line #! shebang compiles with zero error diagnostics and the code
    ''' (Dim x = 1) still has its normal semantics.
    ''' </summary>
    <Fact>
    Public Async Function TestShebangDirective_CompilesAndRuns() As Task
        Dim script = VisualBasicScript.Create(
            "#!/opt/vbi-n2fork/vbi" & vbCrLf &
            "Dim x = 1" & vbCrLf &
            "? x",
            s_defaultOptions)

        Assert.DoesNotContain(script.GetCompilation().GetDiagnostics(), Function(d) d.Severity = DiagnosticSeverity.Error)

        Dim state = Await script.RunAsync()
        Assert.Equal(1, state.ReturnValue)
    End Function

    ''' <summary>
    ''' L3-2: A #! shebang coexists with #R and #Load directives (in-memory source resolver); all
    ''' three directive kinds parse and the loaded code runs.
    ''' </summary>
    <Fact>
    Public Async Function TestShebangDirective_CoexistsWithReferenceAndLoadDirectives() As Task
        Dim files = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From
        {
            {"C:\scripts\loaded.vbx", "Function LoadedValue() As Integer" & vbCrLf & "    Return 42" & vbCrLf & "End Function"},
            {"C:\scripts\main.vbx", "#!shebang" & vbCrLf &
                                   "#R """ & GetType(ScriptTests).Assembly.Location & """" & vbCrLf &
                                   "#Load ""loaded.vbx""" & vbCrLf &
                                   "? LoadedValue()"}
        }

        Dim script = CreateScriptWithLoadDirective(files("C:\scripts\main.vbx"), files)
        Assert.DoesNotContain(script.GetCompilation().GetDiagnostics(), Function(d) d.Severity = DiagnosticSeverity.Error)

        ' Loaded trees are added before the main tree; the shebang/#R/#Load live in the main tree.
        Dim mainTree = script.GetCompilation().SyntaxTrees.Single(Function(t) t.FilePath = "C:\scripts\main.vbx")
        Dim root = DirectCast(mainTree.GetRoot(), CompilationUnitSyntax)
        Assert.Contains(root.GetDirectives(), Function(d) d.Kind = SyntaxKind.ShebangDirectiveTrivia)
        Assert.Equal(1, root.GetReferenceDirectives().Count)
        Assert.Equal(1, root.GetLoadDirectives().Count)

        Dim state = Await script.RunAsync()
        Assert.Equal(42, state.ReturnValue)
    End Function

    ''' <summary>
    ''' L3-3: A .vbx first-line #! followed by a trailing expression evaluates normally
    ''' (script mode does not change its exit/return semantics).
    ''' </summary>
    <Fact>
    Public Async Function TestShebangDirective_TrailingExpressionUnchanged() As Task
        Dim state = Await VisualBasicScript.RunAsync(
            "#!/usr/bin/env -S python" & vbCrLf & "? 1 + 1",
            s_defaultOptions)

        Assert.Equal(2, state.ReturnValue)
    End Function

    ''' <summary>
    ''' A1: A clean script compiles with no error diagnostics. /check's success path relies on this
    ''' (Script.Compile returns warnings only on success). Fully-qualified System.Console because
    ''' ScriptOptions.Default has no global imports.
    ''' </summary>
    <Fact>
    Public Sub TestCompileCleanScriptHasNoErrors()
        Dim diagnostics = VisualBasicScript.Create("System.Console.WriteLine(1)", s_defaultOptions).Compile()

        Assert.False(diagnostics.HasAnyErrors())
        Assert.DoesNotContain(diagnostics, Function(d) d.Severity = DiagnosticSeverity.Error)
    End Sub

    ''' <summary>
    ''' A2: A script with only a warning compiles successfully and reports the warning, no error.
    ''' A top-level "Dim unusedVar As Integer" becomes a submission field and is NOT flagged; an unused
    ''' local inside a method is the canonical BC42024 warning.
    ''' </summary>
    <Fact>
    Public Sub TestCompileWarningScriptHasWarningNoError()
        Dim diagnostics = VisualBasicScript.Create(
            "Sub S()" & vbCrLf &
            "    Dim unusedVar As Integer" & vbCrLf &
            "End Sub",
            s_defaultOptions).Compile()

        Assert.Contains(diagnostics, Function(d) d.Id = "BC42024" AndAlso d.Severity = DiagnosticSeverity.Warning)
        Assert.False(diagnostics.HasAnyErrors())
        Assert.DoesNotContain(diagnostics, Function(d) d.Severity = DiagnosticSeverity.Error)
    End Sub

    ''' <summary>
    ''' A3: A script with an error reports it and HasAnyErrors is true. /check's failure path relies on this.
    ''' </summary>
    <Fact>
    Public Sub TestCompileErrorScriptHasError()
        Dim diagnostics = VisualBasicScript.Create("System.Console.WriteLine(notDeclared)", s_defaultOptions).Compile()

        Assert.True(diagnostics.HasAnyErrors())
        Assert.Contains(diagnostics, Function(d) d.Id = "BC30451")
    End Sub

    ' ---- U9 #6 · '#Load' return semantics (C# ReturnInLoadedFile* / MultipleLoadedFiles* / LoadedFileWithGoto,
    '      CSharpTest\ScriptTests.cs:643,661,684,707,742,777,808,826) ----
    '
    ' Every case here is in memory: the '#Load' targets resolve from InMemorySourceReferenceResolver above and no
    ' file is written. The VB texts are not the C# texts: a VB submission's top level statement list is the body of
    ' the submission method, so an outer 'Return' and a loaded tree's 'Return' compete as returns of one function,
    ' and the loaded tree is an extra tree of the same submission rather than a separate method - which is what makes
    ' the cross tree 'GoTo' case below a diagnostic instead of a jump.

    ''' <summary>
    ''' U9 #6 cell one (C# <c>MultipleLoadedFilesWithReturnAndTrailingExpression</c>, ST:742): with two loaded trees
    ''' the first one that returns decides. 1 (both return) and 20 (only b returns, a declares a field) pin that the
    ''' decision follows the execution order of the trees and not the order of the directives alone; a host that
    ''' looked at the last tree, or at the main tree first, would answer 2 / 30.
    ''' </summary>
    <Fact>
    Public Async Function TestMultipleLoadedFiles_FirstReturnDecides() As Task
        Dim bothReturn = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"C:\scripts\main.vbx", "#Load ""a.vbx""" & vbCrLf & "#Load ""b.vbx"""},
            {"C:\scripts\a.vbx", "Return 1"},
            {"C:\scripts\b.vbx", "Return 2"}
        }

        Dim script = CreateScriptWithLoadDirective(bothReturn("C:\scripts\main.vbx"), bothReturn)
        Assert.DoesNotContain(script.Compile(), Function(d) d.Severity = DiagnosticSeverity.Error)
        Dim state = Await script.RunAsync()
        Assert.Equal(1, state.ReturnValue)

        Dim onlySecondReturns = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"C:\scripts\main.vbx", "#Load ""a.vbx""" & vbCrLf & "#Load ""b.vbx""" & vbCrLf & "Return 30"},
            {"C:\scripts\a.vbx", "Dim fromA As Integer = 1"},
            {"C:\scripts\b.vbx", "Return 20"}
        }

        Dim other = CreateScriptWithLoadDirective(onlySecondReturns("C:\scripts\main.vbx"), onlySecondReturns)
        Dim otherState = Await other.RunAsync()
        Assert.Equal(20, otherState.ReturnValue)
    End Function

    ''' <summary>
    ''' U9 #6 cell two (C# <c>ReturnInLoadedFile</c>, ST:643, and <c>ReturnInLoadedFileTrailingExpression</c>,
    ''' ST:661): the priority between a loaded tree's <c>Return</c> and the main tree's own last statement. A loaded
    ''' <c>Return 42</c> wins over the main tree's <c>Return 17</c> (42), while a loaded tree without a reachable
    ''' return hands the result back to the main tree (17) - including when its <c>Return</c> sits in a branch that is
    ''' not taken. An implementation that inlined the loaded text (the shape issue-vbx-load-span-shift.md is about)
    ''' would answer 17 in the first case, and one that always preferred the main tree would answer 17 both times.
    ''' </summary>
    <Fact>
    Public Async Function TestLoadedFileReturnPrecedesTheMainTreeReturn() As Task
        Dim returning = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"C:\scripts\main.vbx", "#Load ""loaded.vbx""" & vbCrLf & "Return 17"},
            {"C:\scripts\loaded.vbx", "Return 42"}
        }

        Dim script = CreateScriptWithLoadDirective(returning("C:\scripts\main.vbx"), returning)
        Assert.DoesNotContain(script.Compile(), Function(d) d.Severity = DiagnosticSeverity.Error)
        Dim state = Await script.RunAsync()
        Assert.Equal(42, state.ReturnValue)

        Dim notTaken = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"C:\scripts\main.vbx", "#Load ""loaded.vbx""" & vbCrLf & "Return 17"},
            {"C:\scripts\loaded.vbx", "If False Then" & vbCrLf & "    Return 42" & vbCrLf & "End If" & vbCrLf & "? 1"}
        }

        Dim other = CreateScriptWithLoadDirective(notTaken("C:\scripts\main.vbx"), notTaken)
        Dim otherState = Await other.RunAsync()
        Assert.Equal(17, otherState.ReturnValue)
    End Function

    ''' <summary>
    ''' U9 #6 cell three: the loaded tree's own trailing expression is a candidate for the submission's result when
    ''' the main tree has none (<c>? 5</c> alone gives 5). The C# baseline answers null for that shape (ST:707); the
    ''' VB answer is registered here, and the companion case shows the main tree taking the result back as soon as it
    ''' has a value of its own (<c>? 99</c> in the loaded tree, <c>Return 17</c> in the main tree gives 17).
    ''' </summary>
    <Fact>
    Public Async Function TestLoadedFileTrailingExpression_IsTheResultOnlyWithoutAMainTreeValue() As Task
        Dim loadedOnly = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"C:\scripts\main.vbx", "#Load ""loaded.vbx"""},
            {"C:\scripts\loaded.vbx", "? 5"}
        }

        Dim script = CreateScriptWithLoadDirective(loadedOnly("C:\scripts\main.vbx"), loadedOnly)
        Dim state = Await script.RunAsync()
        Assert.Equal(5, state.ReturnValue)

        Dim overridden = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"C:\scripts\main.vbx", "#Load ""loaded.vbx""" & vbCrLf & "Return 17"},
            {"C:\scripts\loaded.vbx", "Dim x As Integer = 1" & vbCrLf & "? 99"}
        }

        Dim other = CreateScriptWithLoadDirective(overridden("C:\scripts\main.vbx"), overridden)
        Dim otherState = Await other.RunAsync()
        Assert.Equal(17, otherState.ReturnValue)
    End Function

    ''' <summary>
    ''' U9 #6 cell four (C# <c>LoadedFileWithReturnAndGoto</c>, ST:777). VB keeps 'GoTo' inside one tree: the loaded
    ''' tree may jump over its own <c>Return 1</c> to a label it declares (2), but a jump across the tree boundary is
    ''' reported as BC30132 at the <c>GoTo</c> line of the referring tree, because the two trees are not one method
    ''' body. Both halves are asserted, so neither "the label is found" nor "the label is not found" can pass alone.
    ''' </summary>
    <Fact>
    Public Async Function TestLoadedFileGoto_StaysInsideItsOwnTree() As Task
        Dim withinOneTree = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"C:\scripts\main.vbx", "#Load ""loaded.vbx"""},
            {"C:\scripts\loaded.vbx", "GoTo done" & vbCrLf & "Return 1" & vbCrLf & "done:" & vbCrLf & "Return 2"}
        }

        Dim script = CreateScriptWithLoadDirective(withinOneTree("C:\scripts\main.vbx"), withinOneTree)
        Assert.DoesNotContain(script.Compile(), Function(d) d.Severity = DiagnosticSeverity.Error)
        Dim state = Await script.RunAsync()
        Assert.Equal(2, state.ReturnValue)

        Dim acrossTrees = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"C:\scripts\main.vbx", "#Load ""loaded.vbx""" & vbCrLf & "GoTo FromLoaded" & vbCrLf & "Return 1"},
            {"C:\scripts\loaded.vbx", "Return 2" & vbCrLf & "FromLoaded:" & vbCrLf & "Return 3"}
        }

        Dim other = CreateScriptWithLoadDirective(acrossTrees("C:\scripts\main.vbx"), acrossTrees)
        Dim unresolvedLabel = other.Compile().Single(Function(d) d.Id = "BC30132")
        Assert.Equal("C:\scripts\main.vbx", unresolvedLabel.Location.GetLineSpan().Path)
        Assert.Equal(2, GetLineNumber(unresolvedLabel))
    End Function

    ''' <summary>
    ''' U9 #6 cell five (C# <c>VoidReturn</c>, ST:808, and <c>LoadedFileWithVoidReturn</c>, ST:826). A bare
    ''' <c>Return</c> in the loaded tree ends the submission with no value at all (Nothing, even though the main tree
    ''' has its own <c>Return 17</c>), and a loaded tree whose only statement is a call produces no value either, so
    ''' the main tree answers. 17 and Nothing are the two discriminators: a host that treated a bare return as "keep
    ''' looking" would answer 17 in the first case.
    ''' </summary>
    <Fact>
    Public Async Function TestLoadedFileBareReturn_EndsTheSubmissionWithoutAValue() As Task
        Dim bareReturn = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"C:\scripts\main.vbx", "#Load ""loaded.vbx""" & vbCrLf & "Return 17"},
            {"C:\scripts\loaded.vbx", "Return"}
        }

        Dim script = CreateScriptWithLoadDirective(bareReturn("C:\scripts\main.vbx"), bareReturn)
        Dim state = Await script.RunAsync()
        Assert.Null(state.ReturnValue)

        Dim voidCall = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"C:\scripts\main.vbx", "#Load ""loaded.vbx""" & vbCrLf & "Return 17"},
            {"C:\scripts\loaded.vbx", "System.Console.WriteLine(42)"}
        }

        Dim other = CreateScriptWithLoadDirective(voidCall("C:\scripts\main.vbx"), voidCall)
        Dim otherState = Await other.RunAsync()
        Assert.Equal(17, otherState.ReturnValue)
    End Function

    ''' <summary>
    ''' U9 #6 cell six: the typed face of the same rule. A typed submission follows Function Main semantics, so a
    ''' loaded tree that returns with no value gives the default of the return type (0) instead of Nothing - the C#
    ''' baseline's <c>LoadedFileWithVoidReturn</c> answer, reached here from the loaded tree rather than from the main
    ''' one. A host that let the main tree's <c>Return 17</c> win would answer 17.
    ''' </summary>
    <Fact>
    Public Async Function TestLoadedFileBareReturn_InATypedSubmissionIsTheDefaultValue() As Task
        Dim files = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"C:\scripts\main.vbx", "#Load ""loaded.vbx""" & vbCrLf & "Return 17"},
            {"C:\scripts\loaded.vbx", "Dim i As Integer = 42" & vbCrLf & "Return" & vbCrLf & "i = -1"}
        }

        Dim options = s_defaultOptions.
            WithFilePath("C:\scripts\main.vbx").
            WithSourceResolver(New InMemorySourceReferenceResolver(files))
        Dim script = VisualBasicScript.Create(Of Integer)(files("C:\scripts\main.vbx"), options)

        Assert.DoesNotContain(script.Compile(), Function(d) d.Severity = DiagnosticSeverity.Error)
        Dim state = Await script.RunAsync()
        Assert.Equal(0, state.ReturnValue)
    End Function

    ' TODO: port C# tests
End Class
