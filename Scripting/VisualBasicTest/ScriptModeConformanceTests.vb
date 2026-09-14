' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System
Imports System.Collections.Generic
Imports System.Collections.Immutable
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.Scripting.Hosting
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting.Hosting
Imports Xunit

''' <summary>
''' Shared host of the script mode conformance matrix (test-plan §5, design-detailed.md §U7).
''' <para>
''' Every cell is exercised through one uniform assertion: the submission either compiles and runs, or it reports
''' diagnostics. The host process has to keep going either way - the assertions that used to end it live in the
''' method body and code generation passes, and both are reached by <c>RunAsync</c> (the emit step runs even when
''' the front end already reported errors, <c>Scripting\Core\ScriptBuilder.cs:133</c>). A <c>Debug.Assert</c> would
''' end this process, so a cell that terminates the run is the strongest failure signal the matrix can produce.
''' </para>
''' <para>
''' Everything here is in memory: no file, process, registry or network access. <c>#Load</c> targets resolve from a
''' <see cref="SourceReferenceResolver"/> over a dictionary and the interactive host reuses an existing directory as
''' its temp directory.
''' </para>
''' </summary>
Friend NotInheritable Class ScriptModeConformance

    Private Sub New()
    End Sub

    ''' <summary>
    ''' Need to create a <see cref="PortableExecutableReference"/> without a file path here. Scripting will
    ''' attempt to validate file paths and one does not exist for this reference as it is an in memory item.
    ''' </summary>
    Friend Shared ReadOnly MsvbReference As PortableExecutableReference = AssemblyMetadata.CreateFromImage(
        File.ReadAllBytes(GetType(Strings).Assembly.Location)).GetReference()

    Friend Shared ReadOnly DefaultOptions As ScriptOptions = ScriptOptions.Default.
        AddReferences(MsvbReference).
        AddReferences(GetType(Task).Assembly).
        AddImports("System.Threading.Tasks")

    ''' <summary>The submission every interactive case ends with: reaching it proves the session survived.</summary>
    Friend Const ReplMarker As String = "? 13 * 29"

    Friend Const ReplMarkerResult As String = "377"

#Region "uniform assertion"

    ''' <summary>
    ''' The uniform matrix assertion, arm one: the cell runs, its result is pinned, and no diagnostic may be
    ''' reported. An internal compiler exception or an assertion failure escapes as a test failure carrying the
    ''' offending source - both are reachable through <c>RunAsync</c>, which emits even when the front end reported
    ''' errors (<c>Scripting\Core\ScriptBuilder.cs:133</c>).
    ''' </summary>
    Friend Shared Sub AssertRuns(source As String, expectedValue As Object, Optional options As ScriptOptions = Nothing)
        Dim script = VisualBasicScript.Create(source, If(options, DefaultOptions))
        Dim diagnostics = script.Compile()
        Dim state = RunToState(script, source, diagnostics)

        Assert.True(state IsNot Nothing, Report("a clean submission has to run", source, diagnostics, Nothing))
        Assert.Equal(expectedValue, state.ReturnValue)
    End Sub

    ''' <summary>
    ''' The uniform matrix assertion, arm two: the cell is rejected, and it is rejected with the expected diagnostic
    ''' - which is what reaches the host. Any other exception type fails the case.
    ''' </summary>
    Friend Shared Sub AssertReports(source As String, expectedId As String, Optional options As ScriptOptions = Nothing)
        Dim script = VisualBasicScript.Create(source, If(options, DefaultOptions))
        Dim diagnostics = script.Compile()

        Assert.True(ErrorDiagnostics(diagnostics).Any(Function(d) d.Id = expectedId),
                    Report("expected " & expectedId, source, diagnostics, Nothing))

        Dim failure = RunToFailure(script)
        Assert.True(TypeOf failure Is CompilationErrorException,
                    Report("the rejected submission must surface as a diagnostic", source, diagnostics, failure))
        Assert.True(DirectCast(failure, CompilationErrorException).Diagnostics.Any(Function(d) d.Id = expectedId),
                    Report("the host did not receive " & expectedId, source, diagnostics, failure))
    End Sub

    ''' <summary>
    ''' The cell has to survive the front end and reach valid metadata, but running it would end this process
    ''' (<c>End</c> / <c>Stop</c>). The emit step is the whole observable surface for those.
    ''' </summary>
    Friend Shared Sub AssertEmits(source As String, Optional options As ScriptOptions = Nothing)
        Dim script = VisualBasicScript.Create(source, If(options, DefaultOptions))
        Dim diagnostics = script.Compile()

        Assert.True(ErrorDiagnostics(diagnostics).Length = 0, Report("a clean submission has to emit", source, diagnostics, Nothing))

        Dim failure As Exception = Nothing
        Dim success = False
        Try
            success = script.GetCompilation().Emit(New MemoryStream()).Success
        Catch ex As Exception
            failure = ex
        End Try

        Assert.True(failure Is Nothing,
                    Report("the emit step must not throw", source, diagnostics, failure))
        Assert.True(success,
                    Report("an error free submission has to produce an artifact", source, diagnostics, Nothing))
    End Sub

    Private Shared Function ErrorDiagnostics(diagnostics As ImmutableArray(Of Diagnostic)) As Diagnostic()
        Return diagnostics.Where(Function(d) d.Severity = DiagnosticSeverity.Error).ToArray()
    End Function

    Private Shared Function RunToState(script As Script(Of Object), source As String, diagnostics As ImmutableArray(Of Diagnostic)) As ScriptState
        Try
            If ErrorDiagnostics(diagnostics).Length = 0 Then
                Return script.RunAsync().GetAwaiter().GetResult()
            End If
        Catch ex As Exception
            Assert.True(False, Report("the submission did not run", source, diagnostics, ex))
        End Try

        Return Nothing
    End Function

    Private Shared Function RunToFailure(script As Script(Of Object)) As Exception
        Try
            script.RunAsync().GetAwaiter().GetResult()
        Catch ex As Exception
            Return ex
        End Try

        Return Nothing
    End Function

    Private Shared Function Report(what As String, source As String, diagnostics As ImmutableArray(Of Diagnostic), failure As Exception) As String
        Dim builder = New StringBuilder()
        builder.AppendLine(what).AppendLine("--- source ---")
        For Each line In source.Replace(vbCrLf, vbLf).Split(ControlChars.Lf)
            builder.AppendLine("| " & line)
        Next

        builder.AppendLine("--- diagnostics ---")
        If diagnostics.IsDefaultOrEmpty Then
            builder.AppendLine("(none)")
        Else
            For Each diagnostic In diagnostics
                builder.AppendLine($"{diagnostic.Id} {diagnostic.Severity} @{diagnostic.Location.GetLineSpan().StartLinePosition.Line}: {diagnostic.GetMessage()}")
            Next
        End If

        builder.AppendLine("--- run ---")
        If failure Is Nothing Then
            builder.AppendLine("(no exception)")
        Else
            Dim frames = failure.ToString().Split(ControlChars.Lf).Take(12)
            builder.AppendLine(failure.GetType().FullName & ": " & failure.Message)
            builder.AppendLine(String.Join(Environment.NewLine, frames))
        End If

        Return builder.ToString()
    End Function

#End Region

#Region "interactive host"

    ''' <summary>
    ''' In-memory console; an existing directory is reused as <c>BuildPaths.TempDir</c> so that nothing is written
    ''' to disk.
    ''' </summary>
    Friend Shared Function CreateRunner(input As String) As CommandLineRunner
        Dim buildPaths = New BuildPaths(
            clientDir:=AppContext.BaseDirectory,
            workingDir:=AppContext.BaseDirectory,
            sdkDir:=RuntimeMetadataReferenceResolver.GetDesktopFrameworkDirectory(),
            tempDir:=AppContext.BaseDirectory)
        Dim compiler = New VisualBasicInteractiveCompiler(
            Path.Combine(AppContext.BaseDirectory, "vbi.rsp"), buildPaths, {"/R:System"}, New NotImplementedAnalyzerLoader())
        Return New CommandLineRunner(New TestConsoleIO(input), compiler, VisualBasicScriptCompiler.Instance, VisualBasicObjectFormatter.Instance)
    End Function

    ''' <summary>
    ''' The interactive half of the uniform assertion: the session has to survive every submission of the case and
    ''' still run the marker submission. The transcript is echoed into the message so a failure is diagnosable from
    ''' the test output alone.
    ''' </summary>
    Friend Shared Sub AssertReplSession(lines As String(), ParamArray expectedOutputs As String())
        Dim runner = CreateRunner(String.Join(vbCrLf, lines) & vbCrLf & ReplMarker & vbCrLf)
        Dim exitCode = runner.RunInteractive()
        Dim [out] = runner.Console.Out.ToString()
        Dim transcript = [out] & vbCrLf & "|| ERROR: " & runner.Console.Error.ToString() & vbCrLf &
                         "|| INPUT: " & String.Join(" / ", lines)

        Assert.True(exitCode = 0, "the session must not fail out: " & transcript)
        Assert.True([out].Contains(vbCrLf & ReplMarkerResult & vbCrLf),
                    "the session did not reach the marker submission: " & transcript)

        For Each expected In expectedOutputs
            Assert.True([out].Contains(vbCrLf & expected & vbCrLf),
                        "missing output '" & expected & "': " & transcript)
        Next
    End Sub

#End Region

#Region "multi tree sources"

    ''' <summary>
    ''' Runs a chain of submissions in which every tree may '#Load' from memory, and returns the last submission's
    ''' return value. The chain is the script API counterpart of the interactive session: each continuation compiles
    ''' against the previous submissions, and a loaded tree is an extra tree of its own submission.
    ''' </summary>
    Friend Shared Function RunChain(files As IReadOnlyDictionary(Of String, String), mainFile As String, ParamArray submissions As String()) As Object
        Dim options = DefaultOptions.WithFilePath(mainFile).WithSourceResolver(New MemorySourceReferenceResolver(files))
        Dim script = VisualBasicScript.Create(submissions(0), options)
        For index = 1 To submissions.Length - 1
            script = script.ContinueWith(submissions(index))
        Next

        Try
            Return script.RunAsync().GetAwaiter().GetResult().ReturnValue
        Catch ex As CompilationErrorException
            Assert.True(False, "the chain was rejected: " & String.Join(", ", ex.Diagnostics.Select(Function(d) d.Id)) &
                               vbCrLf & String.Join(vbCrLf & "--- submission ---" & vbCrLf, submissions))
        Catch ex As Exception
            Assert.True(False, "the chain did not run: " & ex.GetType().FullName & ": " & ex.Message &
                               vbCrLf & String.Join(vbCrLf & "--- submission ---" & vbCrLf, submissions))
        End Try

        Return Nothing
    End Function


    ''' <summary>Resolves <c>#Load</c> targets from memory so that no file has to be written.</summary>
    Friend NotInheritable Class MemorySourceReferenceResolver
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
                Dim combined = IO.Path.Combine(If(IO.Path.GetDirectoryName(baseFilePath), ""), path)
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

#End Region

End Class

''' <summary>
''' Declaration and modifier cells of the matrix (design-detailed.md §U7 维度一/二): every VB declaration form the
''' top level of a script accepts, and the modifiers/attributes that decorate it. A submission class has no base
''' type and no project file, which is what makes these shapes different from the same source in an ordinary
''' compilation.
''' </summary>
Public Class ScriptModeDeclarationConformanceTests

    ' ---- 声明 · Dim ----

    ''' <summary>Dim / As New / array upper bound / ReadOnly, all as top level fields of the submission class.</summary>
    <Fact>
    Public Sub TopLevelDimForms_Conform()
        ScriptModeConformance.AssertRuns(
            "Dim plain As Integer = 1" & vbCrLf &
            "Dim created As New System.Text.StringBuilder" & vbCrLf &
            "Dim arr(2) As Integer" & vbCrLf &
            "ReadOnly ro As Integer = 5" & vbCrLf &
            "Return plain + arr.Length + ro + created.Length", 9)
    End Sub

    ''' <summary>Array initializers, including a jagged one.</summary>
    <Fact>
    Public Sub TopLevelArrayInitializers_Conform()
        ScriptModeConformance.AssertRuns(
            "Dim arr() As Integer = {1, 2, 3}" & vbCrLf &
            "Dim jagged()() As Integer = {New Integer() {1}, New Integer() {2}}" & vbCrLf &
            "Return arr.Length + jagged(1)(0)", 5)
    End Sub

    ' ---- 声明 · Const ----

    ''' <summary>Const of Integer, Date, Decimal and String.</summary>
    <Fact>
    Public Sub TopLevelConstForms_Conform()
        ScriptModeConformance.AssertRuns(
            "Const ci As Integer = 3" & vbCrLf &
            "Const dateValue As Date = #1/1/2020#" & vbCrLf &
            "Const decimalValue As Decimal = 1.5D" & vbCrLf &
            "Const text As String = ""abc""" & vbCrLf &
            "Return ci + dateValue.Year + CInt(decimalValue * 10) + text.Length", 2041)
    End Sub

    ' ---- 声明 · Property ----

    ''' <summary>Auto implemented properties, with and without an initializer.</summary>
    <Fact>
    Public Sub TopLevelAutoProperties_Conform()
        ScriptModeConformance.AssertRuns(
            "Property Auto As Integer" & vbCrLf &
            "Property AutoWithInit As String = ""init""" & vbCrLf &
            "Return Auto & ""|"" & AutoWithInit", "0|init")
    End Sub

    ''' <summary>Hand written property with a backing field.</summary>
    <Fact>
    Public Sub TopLevelHandWrittenProperty_Conforms()
        ScriptModeConformance.AssertRuns(
            "Private backing As Integer" & vbCrLf &
            "Property Value As Integer" & vbCrLf &
            "    Get" & vbCrLf &
            "        Return backing" & vbCrLf &
            "    End Get" & vbCrLf &
            "    Set(newValue As Integer)" & vbCrLf &
            "        backing = newValue" & vbCrLf &
            "    End Set" & vbCrLf &
            "End Property" & vbCrLf &
            "Value = 42" & vbCrLf &
            "Return Value", 42)
    End Sub

    ''' <summary>A default property with a parameter, invoked with the VB indexer syntax.</summary>
    <Fact>
    Public Sub TopLevelDefaultPropertyWithParameters_Conforms()
        ScriptModeConformance.AssertRuns(
            "Private _items(9) As Integer" & vbCrLf &
            "Default Property Indexer(i As Integer) As Integer" & vbCrLf &
            "    Get" & vbCrLf &
            "        Return _items(i)" & vbCrLf &
            "    End Get" & vbCrLf &
            "    Set(newValue As Integer)" & vbCrLf &
            "        _items(i) = newValue" & vbCrLf &
            "    End Set" & vbCrLf &
            "End Property" & vbCrLf &
            "Indexer(1) = 7" & vbCrLf &
            "Return Indexer(1)", 7)
    End Sub

    ''' <summary>ReadOnly and Shared properties: the shared one is initialized by the synthesized shared constructor.</summary>
    <Fact>
    Public Sub TopLevelReadOnlyAndSharedProperties_Conform()
        ScriptModeConformance.AssertRuns(
            "ReadOnly Property ReadOnlyValue As Integer" & vbCrLf &
            "    Get" & vbCrLf &
            "        Return 7" & vbCrLf &
            "    End Get" & vbCrLf &
            "End Property" & vbCrLf &
            "Shared Property SharedValue As Integer" & vbCrLf &
            "Return ReadOnlyValue + SharedValue", 7)
    End Sub

    ' ---- 声明 · Event ----

    ''' <summary>
    ''' A custom event with all three accessors, declared by the submission class; registration goes through the
    ''' accessors the class itself declares. Raising that event is asserted separately below.
    ''' </summary>
    <Fact>
    Public Sub TopLevelCustomEvent_AccessorsAndRegistration_Conform()
        ScriptModeConformance.AssertRuns(
            "Private _handlers As System.EventHandler" & vbCrLf &
            "Custom Event Changed As System.EventHandler" & vbCrLf &
            "    AddHandler(value As System.EventHandler)" & vbCrLf &
            "        _handlers = CType(System.Delegate.Combine(_handlers, value), System.EventHandler)" & vbCrLf &
            "    End AddHandler" & vbCrLf &
            "    RemoveHandler(value As System.EventHandler)" & vbCrLf &
            "        _handlers = CType(System.Delegate.Remove(_handlers, value), System.EventHandler)" & vbCrLf &
            "    End RemoveHandler" & vbCrLf &
            "    RaiseEvent(sender As Object, e As System.EventArgs)" & vbCrLf &
            "        If _handlers IsNot Nothing Then _handlers(sender, e)" & vbCrLf &
            "    End RaiseEvent" & vbCrLf &
            "End Event" & vbCrLf &
            "Dim count As Integer = 0" & vbCrLf &
            "AddHandler Changed, Sub(s As Object, e As System.EventArgs) count += 1" & vbCrLf &
            "RemoveHandler Changed, Sub(s As Object, e As System.EventArgs) count += 1" & vbCrLf &
            "Return count", 0)
    End Sub

    ''' <summary>
    ''' Raising a custom event from a member of the submission class reaches the registered handler. The raise calls
    ''' the <c>RaiseEvent</c> accessor the class declares, so the call receiver is the submission class itself: the
    ''' binder routes unqualified member references to the previous submission reference
    ''' (<c>Binding\Binder_Expressions.vb:2615-2618</c>, <c>TryBindInteractiveReceiver</c>), which
    ''' <c>Lowering\LocalRewriter\LocalRewriter_RaiseEvent.vb:27</c> now treats like an implicit <c>Me</c>.
    ''' </summary>
    <Fact>
    Public Sub TopLevelCustomEventRaise_ReachesTheHandler()
        ScriptModeConformance.AssertRuns(
            "Private _count As Integer" & vbCrLf &
            "Private _handlers As System.EventHandler" & vbCrLf &
            "Custom Event Changed As System.EventHandler" & vbCrLf &
            "    AddHandler(value As System.EventHandler)" & vbCrLf &
            "        _handlers = CType(System.Delegate.Combine(_handlers, value), System.EventHandler)" & vbCrLf &
            "    End AddHandler" & vbCrLf &
            "    RemoveHandler(value As System.EventHandler)" & vbCrLf &
            "        _handlers = CType(System.Delegate.Remove(_handlers, value), System.EventHandler)" & vbCrLf &
            "    End RemoveHandler" & vbCrLf &
            "    RaiseEvent(sender As Object, e As System.EventArgs)" & vbCrLf &
            "        If _handlers IsNot Nothing Then _handlers(sender, e)" & vbCrLf &
            "    End RaiseEvent" & vbCrLf &
            "End Event" & vbCrLf &
            "Sub Hook()" & vbCrLf &
            "    AddHandler Changed, Sub(s As Object, e As System.EventArgs) _count += 1" & vbCrLf &
            "End Sub" & vbCrLf &
            "Sub RaiseIt()" & vbCrLf &
            "    RaiseEvent Changed(Nothing, System.EventArgs.Empty)" & vbCrLf &
            "End Sub" & vbCrLf &
            "Hook()" & vbCrLf &
            "RaiseIt()" & vbCrLf &
            "RaiseIt()" & vbCrLf &
            "Return _count", 2)
    End Sub

    ''' <summary>The same raise inside a top level lambda, which reaches the event through a different body.</summary>
    <Fact>
    Public Sub TopLevelCustomEventRaiseInLambda_ReachesTheHandler()
        ScriptModeConformance.AssertRuns(
            "Private _count As Integer" & vbCrLf &
            "Private _handlers As System.EventHandler" & vbCrLf &
            "Custom Event Changed As System.EventHandler" & vbCrLf &
            "    AddHandler(value As System.EventHandler)" & vbCrLf &
            "        _handlers = CType(System.Delegate.Combine(_handlers, value), System.EventHandler)" & vbCrLf &
            "    End AddHandler" & vbCrLf &
            "    RemoveHandler(value As System.EventHandler)" & vbCrLf &
            "        _handlers = CType(System.Delegate.Remove(_handlers, value), System.EventHandler)" & vbCrLf &
            "    End RemoveHandler" & vbCrLf &
            "    RaiseEvent(sender As Object, e As System.EventArgs)" & vbCrLf &
            "        If _handlers IsNot Nothing Then _handlers(sender, e)" & vbCrLf &
            "    End RaiseEvent" & vbCrLf &
            "End Event" & vbCrLf &
            "Sub Hook()" & vbCrLf &
            "    AddHandler Changed, Sub(s As Object, e As System.EventArgs) _count += 1" & vbCrLf &
            "End Sub" & vbCrLf &
            "Dim raiseIt As System.Action = Sub() RaiseEvent Changed(Nothing, System.EventArgs.Empty)" & vbCrLf &
            "Hook()" & vbCrLf &
            "raiseIt()" & vbCrLf &
            "Return _count", 1)
    End Sub

    ' ---- 声明 · 方法与重载 ----

    ''' <summary>Async Sub and Async Function declared by the submission class.</summary>
    <Fact>
    Public Sub TopLevelAsyncSubAndFunction_Conform()
        ScriptModeConformance.AssertRuns(
            "Async Sub FireAndForget()" & vbCrLf &
            "    Await Task.Delay(1)" & vbCrLf &
            "End Sub" & vbCrLf &
            "Async Function ComputeAsync() As Task(Of Integer)" & vbCrLf &
            "    Await Task.Delay(1)" & vbCrLf &
            "    Return 11" & vbCrLf &
            "End Function" & vbCrLf &
            "FireAndForget()" & vbCrLf &
            "Dim computed = Await ComputeAsync()" & vbCrLf &
            "Return computed", 11)
    End Sub

    ''' <summary>An Iterator method with Yield, consumed by a top level For Each.</summary>
    <Fact>
    Public Sub TopLevelIteratorFunction_Conforms()
        ScriptModeConformance.AssertRuns(
            "Iterator Function CountTo(n As Integer) As System.Collections.Generic.IEnumerable(Of Integer)" & vbCrLf &
            "    For i = 1 To n" & vbCrLf &
            "        Yield i" & vbCrLf &
            "    Next" & vbCrLf &
            "End Function" & vbCrLf &
            "Dim total = 0" & vbCrLf &
            "For Each value In CountTo(4)" & vbCrLf &
            "    total += value" & vbCrLf &
            "Next" & vbCrLf &
            "Return total", 10)
    End Sub

    ''' <summary>Overloads, resolved from a top level call site.</summary>
    <Fact>
    Public Sub TopLevelOverloads_Conform()
        ScriptModeConformance.AssertRuns(
            "Overloads Function Describe(x As Integer) As String" & vbCrLf &
            "    Return ""I"" & x" & vbCrLf &
            "End Function" & vbCrLf &
            "Overloads Function Describe(x As String) As String" & vbCrLf &
            "    Return ""S"" & x" & vbCrLf &
            "End Function" & vbCrLf &
            "Return Describe(1) & Describe(""a"")", "I1Sa")
    End Sub

    ' ---- 声明 · Operator ----

    ''' <summary>Every operator kind VB supports in a user type: unary, binary, conversion, Widening, Narrowing, IsTrue/IsFalse.</summary>
    <Fact>
    Public Sub TopLevelOperators_Conform()
        ScriptModeConformance.AssertRuns(
            "Structure Money" & vbCrLf &
            "    Public Amount As Decimal" & vbCrLf &
            "    Public Shared Operator +(a As Money, b As Money) As Money" & vbCrLf &
            "        Return New Money With {.Amount = a.Amount + b.Amount}" & vbCrLf &
            "    End Operator" & vbCrLf &
            "    Public Shared Operator -(a As Money) As Money" & vbCrLf &
            "        Return New Money With {.Amount = -a.Amount}" & vbCrLf &
            "    End Operator" & vbCrLf &
            "    Public Shared Operator =(a As Money, b As Money) As Boolean" & vbCrLf &
            "        Return a.Amount = b.Amount" & vbCrLf &
            "    End Operator" & vbCrLf &
            "    Public Shared Operator <>(a As Money, b As Money) As Boolean" & vbCrLf &
            "        Return a.Amount <> b.Amount" & vbCrLf &
            "    End Operator" & vbCrLf &
            "    Public Shared Widening Operator CType(a As Money) As Decimal" & vbCrLf &
            "        Return a.Amount" & vbCrLf &
            "    End Operator" & vbCrLf &
            "    Public Shared Narrowing Operator CType(d As Decimal) As Money" & vbCrLf &
            "        Return New Money With {.Amount = d}" & vbCrLf &
            "    End Operator" & vbCrLf &
            "    Public Shared Operator IsTrue(a As Money) As Boolean" & vbCrLf &
            "        Return a.Amount <> 0D" & vbCrLf &
            "    End Operator" & vbCrLf &
            "    Public Shared Operator IsFalse(a As Money) As Boolean" & vbCrLf &
            "        Return a.Amount = 0D" & vbCrLf &
            "    End Operator" & vbCrLf &
            "End Structure" & vbCrLf &
            "Dim sum As Money = CType(2.5D, Money) + CType(1D, Money)" & vbCrLf &
            "Dim plain As Decimal = sum" & vbCrLf &
            "If sum Then plain += 1D" & vbCrLf &
            "Return CInt(plain * 10)", 45)
    End Sub

    ' ---- 声明 · 嵌套类型 ----

    ''' <summary>Class, Structure, Interface, Enum and Delegate nested in the submission class.</summary>
    <Fact>
    Public Sub NestedTypeDeclarations_Conform()
        ScriptModeConformance.AssertRuns(
            "Class Widget" & vbCrLf &
            "    Public Value As Integer" & vbCrLf &
            "End Class" & vbCrLf &
            "Structure Point" & vbCrLf &
            "    Public X As Integer" & vbCrLf &
            "End Structure" & vbCrLf &
            "Interface IShape" & vbCrLf &
            "    Function Area() As Integer" & vbCrLf &
            "End Interface" & vbCrLf &
            "Enum Kind" & vbCrLf &
            "    First = 1" & vbCrLf &
            "    Second = 2" & vbCrLf &
            "End Enum" & vbCrLf &
            "Delegate Function Transform(x As Integer) As Integer" & vbCrLf &
            "Dim madeWidget As New Widget With {.Value = 2}" & vbCrLf &
            "Dim madePoint As New Point With {.X = 3}" & vbCrLf &
            "Dim madeTransform As Transform = Function(x) x + CInt(Kind.Second)" & vbCrLf &
            "Return madeTransform(madeWidget.Value + madePoint.X)", 7)
    End Sub

    ''' <summary>A Module declared by the submission class.</summary>
    <Fact>
    Public Sub NestedModuleDeclaration_Conforms()
        ScriptModeConformance.AssertRuns(
            "Module Helpers" & vbCrLf &
            "    Public Function Twice(x As Integer) As Integer" & vbCrLf &
            "        Return x * 2" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Module" & vbCrLf &
            "Return Helpers.Twice(3)", 6)
    End Sub

    ''' <summary>Inherits, Implements, MustInherit/MustOverride and NotOverridable in one hierarchy.</summary>
    <Fact>
    Public Sub NestedInheritanceHierarchy_Conforms()
        ScriptModeConformance.AssertRuns(
            "Interface IShape" & vbCrLf &
            "    Function Area() As Double" & vbCrLf &
            "End Interface" & vbCrLf &
            "MustInherit Class ShapeBase" & vbCrLf &
            "    Public MustOverride Function Area() As Double" & vbCrLf &
            "    Public NotOverridable Overrides Function ToString() As String" & vbCrLf &
            "        Return ""shape""" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Class" & vbCrLf &
            "Class Square" & vbCrLf &
            "    Inherits ShapeBase" & vbCrLf &
            "    Implements IShape" & vbCrLf &
            "    Public Side As Double" & vbCrLf &
            "    Public Overrides Function Area() As Double Implements IShape.Area" & vbCrLf &
            "        Return Side * Side" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim shape As ShapeBase = New Square With {.Side = 3.0}" & vbCrLf &
            "Return CInt(shape.Area()) & shape.ToString()", "9shape")
    End Sub

    ''' <summary>Generic types with constraints, plus a generic method.</summary>
    <Fact>
    Public Sub NestedGenericTypesAndConstraints_Conform()
        ScriptModeConformance.AssertRuns(
            "Class Box(Of T As {Class, New})" & vbCrLf &
            "    Public Item As T" & vbCrLf &
            "    Public Sub New()" & vbCrLf &
            "        Item = New T()" & vbCrLf &
            "    End Sub" & vbCrLf &
            "End Class" & vbCrLf &
            "Class Holder(Of T As {Structure, System.IComparable})" & vbCrLf &
            "    Public Value As T" & vbCrLf &
            "End Class" & vbCrLf &
            "Function FirstOf(Of T)(items As System.Collections.Generic.IEnumerable(Of T)) As T" & vbCrLf &
            "    For Each item In items" & vbCrLf &
            "        Return item" & vbCrLf &
            "    Next" & vbCrLf &
            "    Return Nothing" & vbCrLf &
            "End Function" & vbCrLf &
            "Dim madeBox As New Box(Of System.Text.StringBuilder)" & vbCrLf &
            "Dim madeHolder As New Holder(Of Integer)" & vbCrLf &
            "Return FirstOf({1, 2, 3}) + madeHolder.Value + madeBox.Item.Length", 1)
    End Sub

    ''' <summary>Partial types declared in more than one place inside the submission class.</summary>
    <Fact>
    Public Sub NestedPartialTypes_Conform()
        ScriptModeConformance.AssertRuns(
            "Partial Class Part" & vbCrLf &
            "    Public A As Integer" & vbCrLf &
            "End Class" & vbCrLf &
            "Partial Class Part" & vbCrLf &
            "    Public B As Integer" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim made As New Part With {.A = 1, .B = 2}" & vbCrLf &
            "Return made.A + made.B", 3)
    End Sub

    ' ---- 修饰与属性 ----

    ''' <summary>A Shared extension method declared by the submission class and used from a top level call site.</summary>
    <Fact>
    Public Sub TopLevelSharedExtensionMethod_Conforms()
        ScriptModeConformance.AssertRuns(
            "Imports System.Runtime.CompilerServices" & vbCrLf &
            "<Extension>" & vbCrLf &
            "Shared Function Twice(s As String) As String" & vbCrLf &
            "    Return s & s" & vbCrLf &
            "End Function" & vbCrLf &
            "Return ""abc"".Twice()", "abcabc")
    End Sub

    ''' <summary>Obsolete, Conditional and MethodImpl attributes on members of the submission class.</summary>
    <Fact>
    Public Sub TopLevelMemberAttributes_Conform()
        ScriptModeConformance.AssertRuns(
            "Imports System.Diagnostics" & vbCrLf &
            "Imports System.Runtime.CompilerServices" & vbCrLf &
            "<System.Obsolete(""old"")>" & vbCrLf &
            "Sub Deprecated()" & vbCrLf &
            "End Sub" & vbCrLf &
            "<Conditional(""DEBUG"")>" & vbCrLf &
            "Sub DebugOnly()" & vbCrLf &
            "End Sub" & vbCrLf &
            "<MethodImpl(MethodImplOptions.NoInlining)>" & vbCrLf &
            "Sub NotInlined()" & vbCrLf &
            "End Sub" & vbCrLf &
            "NotInlined()" & vbCrLf &
            "Return ""ATTRS""", "ATTRS")
    End Sub

    ''' <summary>Object members overridden by the submission class.</summary>
    <Fact>
    Public Sub TopLevelObjectOverrides_Conform()
        ScriptModeConformance.AssertRuns(
            "Public Overrides Function ToString() As String" & vbCrLf &
            "    Return ""TOP""" & vbCrLf &
            "End Function" & vbCrLf &
            "Public Overrides Function GetHashCode() As Integer" & vbCrLf &
            "    Return 42" & vbCrLf &
            "End Function" & vbCrLf &
            "Public Overrides Function Equals(other As Object) As Boolean" & vbCrLf &
            "    Return other IsNot Nothing" & vbCrLf &
            "End Function" & vbCrLf &
            "Return ToString() & GetHashCode() & Equals(Nothing)", "TOP42False")
    End Sub

End Class
