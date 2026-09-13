' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System
Imports System.Collections.Generic
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
''' A binding error that is reported while a field/property initializer is bound must surface as an ordinary
''' compilation error instead of terminating the host process inside code generation
''' (<c>CodeGen\EmitExpression.vb:207</c>: "Code gen should not be invoked if there are errors.").
''' <para>
''' These cases exercise the host-visible symptom: the script/submission compile used to end in a
''' <c>Debug.Assert</c> while emitting (which is what the <c>vbi</c> probes reported as exit code 35 with no
''' diagnostics at all). Everything here is in-memory: no file, process, registry or network access.
''' </para>
''' </summary>
Public Class ScriptTopLevelCrashTests

    ''' <summary>
    ''' Need to create a <see cref="PortableExecutableReference"/> without a file path here. Scripting will
    ''' attempt to validate file paths and one does not exist for this reference as it's an in memory item.
    ''' </summary>
    Private Shared ReadOnly s_msvbReference As PortableExecutableReference = AssemblyMetadata.CreateFromImage(
        File.ReadAllBytes(GetType(Strings).Assembly.Location)).GetReference()

    Private Shared ReadOnly s_defaultOptions As ScriptOptions = ScriptOptions.Default.
        AddReferences(s_msvbReference).
        AddReferences(GetType(Task).Assembly).
        AddImports("System.Threading.Tasks")

    ''' <summary>A nested class (non-script) initializer is not an async context, so BC36937 is reported.</summary>
    Private Shared ReadOnly AwaitInitializerSource As String =
        "Class C" & vbCrLf &
        "    Dim s As Integer = Await Task.FromResult(9)" & vbCrLf &
        "End Class"

    ''' <summary>
    ''' A top-level statement is bound by the same entry, filed into the instance bucket of the submission class
    ''' and diagnosed into the same bag as the field/property initializers, so an error only it reports (BC30582:
    ''' a <c>SyncLock</c> operand of a non-reference type) has to reach the host as a diagnostic as well.
    ''' </summary>
    Private Shared ReadOnly TopLevelStatementErrorSource As String =
        "SyncLock 5" & vbCrLf &
        "    System.Console.WriteLine(""lock"")" & vbCrLf &
        "End SyncLock" & vbCrLf &
        "System.Console.WriteLine(""after"")"

    <Fact>
    Public Sub NestedClassAwaitInitializer_IsReportedInsteadOfTerminatingTheProcess()
        Dim script = VisualBasicScript.Create(AwaitInitializerSource, s_defaultOptions)

        Dim diagnostics = script.Compile()

        Assert.Contains(diagnostics, Function(d) d.Id = "BC36937" AndAlso d.Severity = DiagnosticSeverity.Error)

        ' The failed submission is reported through the regular error channel ...
        Dim ex = Assert.Throws(Of CompilationErrorException)(Sub() script.RunAsync().GetAwaiter().GetResult())
        Assert.Contains(ex.Diagnostics, Function(d) d.Id = "BC36937")
    End Sub

    <Fact>
    Public Sub TopLevelStatementError_IsReportedInsteadOfReachingCodeGeneration()
        Dim script = VisualBasicScript.Create(TopLevelStatementErrorSource, s_defaultOptions)

        Dim diagnostics = script.Compile()

        Assert.Contains(diagnostics, Function(d) d.Id = "BC30582" AndAlso d.Severity = DiagnosticSeverity.Error)
        Assert.DoesNotContain(diagnostics, Function(d) d.Id = "BC36937")

        Dim ex = Assert.Throws(Of CompilationErrorException)(Sub() script.RunAsync().GetAwaiter().GetResult())
        Assert.Contains(ex.Diagnostics, Function(d) d.Id = "BC30582")
    End Sub

    <Fact>
    Public Sub ReplNestedClassAwaitInitializer_IsReportedAndTheSessionContinues()
        ' Multi-line submission: the REPL keeps buffering until the class declaration is complete.
        Dim runner = CreateRunner(input:=
            "Class C" & vbCrLf &
            "    Dim s As Integer = Await System.Threading.Tasks.Task.FromResult(9)" & vbCrLf &
            "End Class" & vbCrLf &
            "? 1 + 2" & vbCrLf)

        runner.RunInteractive()

        Assert.Contains("BC36937", runner.Console.Error.ToString())

        ' The session survives the failed submission: the next submission runs.
        Assert.Contains(vbCrLf & "3" & vbCrLf, runner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' The statement-kind variant of the session-continuity case: the offending submission holds a top-level
    ''' statement, not a nested class.
    ''' </summary>
    <Fact>
    Public Sub ReplTopLevelStatementError_IsReportedAndTheSessionContinues()
        Dim runner = CreateRunner(input:=
            "SyncLock 5" & vbCrLf &
            "    System.Console.WriteLine(""lock"")" & vbCrLf &
            "End SyncLock" & vbCrLf &
            "? 1 + 2" & vbCrLf)

        runner.RunInteractive()

        Assert.Contains("BC30582", runner.Console.Error.ToString())

        ' The session survives the failed submission: the next submission runs.
        Assert.Contains(vbCrLf & "3" & vbCrLf, runner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' A top-level <c>Shared</c> initializer used to be injected into the submission constructor, whose
    ''' <c>submissionArray</c> parameter then ended up on a <c>.cctor</c>. The type was unloadable and the host
    ''' reported <c>TypeLoadException</c> out of <c>ScriptBuilder.GetEntryPointRuntimeMethod</c>. Reading the field
    ''' back proves that the initializer now runs in the parameterless shared constructor of the submission.
    ''' </summary>
    <Fact>
    Public Async Function TopLevelSharedFieldInitializer_RunsAndTheSubmissionLoads() As Task
        Dim state = Await VisualBasicScript.RunAsync(
            "Shared sx As Integer = 5" & vbCrLf &
            "Return sx", s_defaultOptions)

        Assert.Equal(5, state.ReturnValue)
    End Function

    <Fact>
    Public Async Function TopLevelSharedReadOnlyFieldInitializer_RunsAndTheSubmissionLoads() As Task
        Dim state = Await VisualBasicScript.RunAsync(
            "Shared ReadOnly sx As Integer = 5" & vbCrLf &
            "Return sx", s_defaultOptions)

        Assert.Equal(5, state.ReturnValue)
    End Function

    ''' <summary>
    ''' An array field with an implicit upper bound needs no <c>=</c> to require the shared constructor.
    ''' </summary>
    <Fact>
    Public Async Function TopLevelSharedArrayFieldUpperBound_RunsAndTheSubmissionLoads() As Task
        Dim state = Await VisualBasicScript.RunAsync(
            "Shared arr(2) As Integer" & vbCrLf &
            "Return arr.Length", s_defaultOptions)

        Assert.Equal(3, state.ReturnValue)
    End Function

    ''' <summary>
    ''' A <c>Const</c> field of type <c>Date</c> is injected by a second mechanism that stands down when a
    ''' <c>.cctor</c> is already a member of the type. Both initializers have to end up in that one constructor,
    ''' which is what the two values read back here show.
    ''' </summary>
    <Fact>
    Public Async Function TopLevelConstDateAndSharedFieldInitializers_BothRun() As Task
        Dim state = Await VisualBasicScript.RunAsync(
            "Const d As Date = #1/1/2020#" & vbCrLf &
            "Shared x As Integer = 5" & vbCrLf &
            "Return x * 10000 + d.Year", s_defaultOptions)

        Assert.Equal(52020, state.ReturnValue)
    End Function

    ''' <summary>
    ''' An instance initializer keeps going through the <c>&lt;Initialize&gt;</c> path and does not produce a
    ''' shared constructor.
    ''' </summary>
    <Fact>
    Public Async Function TopLevelInstanceFieldInitializer_StillRuns() As Task
        Dim state = Await VisualBasicScript.RunAsync(
            "Dim iy As Integer = 7" & vbCrLf &
            "Return iy", s_defaultOptions)

        Assert.Equal(7, state.ReturnValue)
    End Function

    ''' <summary>
    ''' A top-level label used to be dropped from the instance initializer sequence, so a <c>GoTo</c> branched to a
    ''' target that never reached the body and code generation crashed. The label is an ordinary executable
    ''' statement of the <c>&lt;Initialize&gt;</c> method, so the jump has to take effect - including across an
    ''' <c>Await</c>.
    ''' </summary>
    <Fact>
    Public Async Function TopLevelGoTo_JumpTakesEffect() As Task
        Dim state = Await VisualBasicScript.RunAsync(
            "Dim log = """"" & vbCrLf &
            "log &= ""A""" & vbCrLf &
            "GoTo skip" & vbCrLf &
            "log &= ""B""" & vbCrLf &
            "skip:" & vbCrLf &
            "Await Task.Delay(1)" & vbCrLf &
            "log &= ""C""" & vbCrLf &
            "Return log", s_defaultOptions)

        Assert.Equal("AC", state.ReturnValue)
    End Function

    <Fact>
    Public Async Function TopLevelBackwardGoTo_Loops() As Task
        Dim state = Await VisualBasicScript.RunAsync(
            "Dim i As Integer = 0" & vbCrLf &
            "top:" & vbCrLf &
            "i += 1" & vbCrLf &
            "If i < 3 Then GoTo top" & vbCrLf &
            "Return i", s_defaultOptions)

        Assert.Equal(3, state.ReturnValue)
    End Function

    ''' <summary>
    ''' Binding the label statements makes the duplicate label check reachable, which is a behaviour change: the
    ''' script used to be compiled without any diagnostic at all.
    ''' </summary>
    <Fact>
    Public Sub TopLevelDuplicateLabel_IsReportedInsteadOfSilentlyPassing()
        Dim script = VisualBasicScript.Create(
            "skip:" & vbCrLf &
            "System.Console.WriteLine(""B"")" & vbCrLf &
            "skip:" & vbCrLf &
            "System.Console.WriteLine(""C"")", s_defaultOptions)

        Dim diagnostics = script.Compile()

        Assert.True(diagnostics.Where(Function(d) d.Severity = DiagnosticSeverity.Error).Count() = 1,
                    String.Join(" | ", diagnostics))
        Assert.Contains(diagnostics, Function(d) d.Id = "BC30094" AndAlso d.Severity = DiagnosticSeverity.Error)

        Dim ex = Assert.Throws(Of CompilationErrorException)(Sub() script.RunAsync().GetAwaiter().GetResult())
        Assert.Contains(ex.Diagnostics, Function(d) d.Id = "BC30094")
    End Sub

    ''' <summary>
    ''' The REPL counterpart of a taken jump. A label has to be the first statement on its line
    ''' (<c>Parser\ParseStatement.vb:1607-1609</c>) and a submission is executed as soon as its buffer is a
    ''' complete submission, so only a backward jump fits into a REPL submission.
    ''' </summary>
    <Fact>
    Public Sub ReplTopLevelBackwardGoTo_JumpTakesEffectAndTheSessionContinues()
        Dim runner = CreateRunner(input:=
            "Dim i As Integer = 0" & vbCrLf &
            "fin: i += 1 : If i < 3 Then GoTo fin" & vbCrLf &
            "? i" & vbCrLf &
            "? 2 + 3" & vbCrLf)

        Dim exitCode = runner.RunInteractive()
        Dim transcript = runner.Console.Out.ToString() & " || " & runner.Console.Error.ToString()

        Assert.True(exitCode = 0, transcript)
        Assert.True(runner.Console.Error.ToString() = "", transcript)

        ' The loop ran to completion (3, not 1) ...
        Assert.True(runner.Console.Out.ToString().Contains(vbCrLf & "3" & vbCrLf), transcript)

        ' ... and the session survives the submission with the jump: the next submission runs.
        Assert.True(runner.Console.Out.ToString().Contains(vbCrLf & "5" & vbCrLf), transcript)
    End Sub

    ''' <summary>
    ''' A submission class is not implicitly declared in the sense the synthesized event members meant: they used to
    ''' assert on that and terminated the process while emitting. The synthesized add/remove accessors are emitted
    ''' and run.
    ''' <para>
    ''' The delivery itself (raise -&gt; handler count) cannot be observed for an <em>instance</em> event declared by
    ''' the submission class: <c>RaiseEvent</c> is not a supported top-level statement, direct access to the event is
    ''' rejected (BC32022) and a <c>RaiseEvent</c> inside a top-level instance <c>Sub</c> or lambda trips a separate,
    ''' unrelated assertion (<c>LocalRewriter_RaiseEvent.vb:36</c>, a container-agnostic shape that predates this
    ''' change). Delivery is therefore covered by the <c>Shared Event</c> and <c>WithEvents</c> cases below.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Async Function TopLevelEvent_AccessorsRun() As Task
        Dim state = Await VisualBasicScript.RunAsync(
            "Event E As System.EventHandler" & vbCrLf &
            "Dim count = 0" & vbCrLf &
            "Dim h As System.EventHandler = Sub(s As Object, e As System.EventArgs)" & vbCrLf &
            "                                   count += 1" & vbCrLf &
            "                               End Sub" & vbCrLf &
            "AddHandler E, h" & vbCrLf &
            "RemoveHandler E, h" & vbCrLf &
            "Return ""ACCESSORS-OK""", s_defaultOptions)

        Assert.Equal("ACCESSORS-OK", state.ReturnValue)
    End Function

    ''' <summary>
    ''' A <c>Shared Event</c> declared by the submission class: registration goes through the synthesized add/remove
    ''' accessors and the delivery really reaches the handler. A top-level <c>Shared Sub</c> is what raises it - the
    ''' same <c>RaiseEvent</c> in an instance member trips the unrelated assertion mentioned above, which is why the
    ''' instance shape is covered by accessor registration only.
    ''' </summary>
    <Fact>
    Public Async Function TopLevelSharedEvent_RaiseReachesTheHandler() As Task
        Dim state = Await VisualBasicScript.RunAsync(
            "Shared Event E As System.EventHandler" & vbCrLf &
            "Shared Sub RaiseIt()" & vbCrLf &
            "    RaiseEvent E(Nothing, System.EventArgs.Empty)" & vbCrLf &
            "End Sub" & vbCrLf &
            "Dim count = 0" & vbCrLf &
            "AddHandler E, Sub(s As Object, e As System.EventArgs)" & vbCrLf &
            "                  count += 1" & vbCrLf &
            "              End Sub" & vbCrLf &
            "RaiseIt()" & vbCrLf &
            "Return count", s_defaultOptions)

        Assert.Equal(1, state.ReturnValue)
    End Function

    ''' <summary>
    ''' A <c>WithEvents</c> field of the submission class: the hookup really delivers - the handler is invoked when
    ''' the nested class raises its event.
    ''' </summary>
    <Fact>
    Public Async Function TopLevelWithEventsField_HookIsEffective() As Task
        Dim state = Await VisualBasicScript.RunAsync(
            RaiserAndHookupSource & vbCrLf &
            "WithEvents r As New Raiser" & vbCrLf &
            "Dim count = 0" & vbCrLf &
            "AddHandler r.SomethingHappened, Sub(s As Object, e As System.EventArgs)" & vbCrLf &
            "                                     count += 1" & vbCrLf &
            "                                 End Sub" & vbCrLf &
            "r.RaiseIt()" & vbCrLf &
            "Return count", s_defaultOptions)

        Assert.Equal(1, state.ReturnValue)
    End Function

    <Fact>
    Public Async Function TopLevelSharedWithEventsField_HookIsEffective() As Task
        Dim state = Await VisualBasicScript.RunAsync(
            RaiserAndHookupSource & vbCrLf &
            "Shared WithEvents r As New Raiser" & vbCrLf &
            "Dim count = 0" & vbCrLf &
            "AddHandler r.SomethingHappened, Sub(s As Object, e As System.EventArgs)" & vbCrLf &
            "                                     count += 1" & vbCrLf &
            "                                 End Sub" & vbCrLf &
            "r.RaiseIt()" & vbCrLf &
            "Return count", s_defaultOptions)

        Assert.Equal(1, state.ReturnValue)
    End Function

    ''' <summary>The REPL variant: a submission declaring an event must not terminate the session.</summary>
    <Fact>
    Public Sub ReplTopLevelEvent_CompilesAndTheSessionContinues()
        Dim runner = CreateRunner(input:=
            "Event E As System.EventHandler" & vbCrLf &
            "? 1 + 2" & vbCrLf)

        Dim exitCode = runner.RunInteractive()
        Dim transcript = runner.Console.Out.ToString() & " || " & runner.Console.Error.ToString()

        Assert.True(exitCode = 0, transcript)
        Assert.True(runner.Console.Error.ToString() = "", transcript)

        ' The session survives the submission with the event: the next submission runs.
        Assert.True(runner.Console.Out.ToString().Contains(vbCrLf & "3" & vbCrLf), transcript)
    End Sub

    ''' <summary>
    ''' Top level 'Await' inside a 'Catch' block: the compile reported no diagnostic at all and the emit phase
    ''' dereferenced a missing branch destination, so the host process ended in an unhandled
    ''' <c>NullReferenceException</c>. The method-body check (BC36943, <c>BindMethodBlock</c>) never saw top level
    ''' statements, whose host is the synthesized initializer with a stub body.
    ''' </summary>
    <Fact>
    Public Sub TopLevelAwaitInCatch_IsReportedInsteadOfReachingCodeGeneration()
        Dim script = VisualBasicScript.Create(
            "Try" & vbCrLf &
            "    System.Console.WriteLine(""TRY"")" & vbCrLf &
            "Catch ex As System.Exception" & vbCrLf &
            "    Await Task.Delay(1)" & vbCrLf &
            "End Try", s_defaultOptions)

        Dim diagnostics = script.Compile()

        Assert.Contains(diagnostics, Function(d) d.Id = "BC36943" AndAlso d.Severity = DiagnosticSeverity.Error)

        Dim ex = Assert.Throws(Of CompilationErrorException)(Sub() script.RunAsync().GetAwaiter().GetResult())
        Assert.Contains(ex.Diagnostics, Function(d) d.Id = "BC36943")
    End Sub

    ''' <summary>
    ''' The 'SyncLock' sub-shape does not crash the compiler: it used to compile and then fail at run time with
    ''' <c>SynchronizationLockException</c>, which is the broken artifact this diagnostic now keeps out of code
    ''' generation.
    ''' </summary>
    <Fact>
    Public Sub TopLevelAwaitInSyncLock_IsReportedInsteadOfProducingABrokenArtifact()
        Dim script = VisualBasicScript.Create(
            "Dim gate As New Object" & vbCrLf &
            "SyncLock gate" & vbCrLf &
            "    Await Task.Delay(1)" & vbCrLf &
            "    System.Console.WriteLine(""IN-LOCK"")" & vbCrLf &
            "End SyncLock", s_defaultOptions)

        Dim diagnostics = script.Compile()

        Assert.Contains(diagnostics, Function(d) d.Id = "BC36943" AndAlso d.Severity = DiagnosticSeverity.Error)

        Dim ex = Assert.Throws(Of CompilationErrorException)(Sub() script.RunAsync().GetAwaiter().GetResult())
        Assert.Contains(ex.Diagnostics, Function(d) d.Id = "BC36943")
    End Sub

    ''' <summary>
    ''' A shared method body of the submission class has no instance to offer, so an implicit reference to an instance
    ''' field is an ordinary BC30369. It used to compile without any diagnostic and the JIT rejected the produced
    ''' method (<c>InvalidProgramException</c>).
    ''' </summary>
    <Fact>
    Public Sub TopLevelSharedMethodBody_ReadingInstanceField_IsReportedInsteadOfInvalidProgram()
        Dim script = VisualBasicScript.Create(
            "Dim sx As Integer = 5" & vbCrLf &
            "Shared Sub S()" & vbCrLf &
            "    System.Console.WriteLine(""SHARED-READS-INSTANCE "" & sx)" & vbCrLf &
            "End Sub" & vbCrLf &
            "S()", s_defaultOptions)

        Dim diagnostics = script.Compile()

        Assert.Contains(diagnostics, Function(d) d.Id = "BC30369" AndAlso d.Severity = DiagnosticSeverity.Error)

        Dim ex = Assert.Throws(Of CompilationErrorException)(Sub() script.RunAsync().GetAwaiter().GetResult())
        Assert.Contains(ex.Diagnostics, Function(d) d.Id = "BC30369")
    End Sub

    ''' <summary>
    ''' A shared initializer of the submission class runs in the shared constructor, so an 'Await' in it has no
    ''' asynchronous context to belong to. It used to survive into code generation and terminate the process
    ''' (exit code 35 from the <c>vbi</c> probes, with no diagnostic at all).
    ''' </summary>
    <Fact>
    Public Sub TopLevelSharedInitializerAwait_IsReportedInsteadOfTerminatingTheProcess()
        Dim script = VisualBasicScript.Create(
            "Shared Dim x = Await Task.FromResult(1)", s_defaultOptions)

        Dim diagnostics = script.Compile()

        Assert.Contains(diagnostics, Function(d) d.Id = "BC37341" AndAlso d.Severity = DiagnosticSeverity.Error)

        Dim ex = Assert.Throws(Of CompilationErrorException)(Sub() script.RunAsync().GetAwaiter().GetResult())
        Assert.Contains(ex.Diagnostics, Function(d) d.Id = "BC37341")
    End Sub

    ''' <summary>
    ''' An '<c>Extension</c>' member of the submission class has to be 'Shared'. The missing diagnostic was a
    ''' <c>Debug.Assert(Me.IsShared)</c> that ended the process while the attribute was decoded.
    ''' </summary>
    <Fact>
    Public Sub TopLevelExtensionMethodWithoutShared_IsReportedInsteadOfTerminatingTheProcess()
        Dim script = VisualBasicScript.Create(
            "Imports System.Runtime.CompilerServices" & vbCrLf &
            "<Extension>" & vbCrLf &
            "Function Twice(s As String) As String" & vbCrLf &
            "    Return s & s" & vbCrLf &
            "End Function" & vbCrLf &
            "Return ""abc"".Twice()", s_defaultOptions)

        Dim diagnostics = script.Compile()

        Assert.Contains(diagnostics, Function(d) d.Id = "BC37005" AndAlso d.Severity = DiagnosticSeverity.Error)

        Dim ex = Assert.Throws(Of CompilationErrorException)(Sub() script.RunAsync().GetAwaiter().GetResult())
        Assert.Contains(ex.Diagnostics, Function(d) d.Id = "BC37005")
    End Sub

    ''' <summary>
    ''' A submission with a top level 'Await' inside a 'Catch' or a 'SyncLock' block reports BC36943 and the session
    ''' survives it. Neither shape is allowed to end the process (the pre-diagnostic symptoms were an emit phase
    ''' crash and a run time <c>SynchronizationLockException</c> respectively).
    ''' </summary>
    <Fact>
    Public Sub ReplTopLevelAwaitInCatchAndSyncLock_IsReportedAndTheSessionContinues()
        Dim runner = CreateRunner(input:=
            "Try" & vbCrLf &
            "    System.Console.WriteLine(""TRY"")" & vbCrLf &
            "Catch ex As System.Exception" & vbCrLf &
            "    Await System.Threading.Tasks.Task.Delay(1)" & vbCrLf &
            "End Try" & vbCrLf &
            "Dim gate As New Object" & vbCrLf &
            "SyncLock gate" & vbCrLf &
            "    Await System.Threading.Tasks.Task.Delay(1)" & vbCrLf &
            "End SyncLock" & vbCrLf &
            "? 1 + 2" & vbCrLf)

        runner.RunInteractive()

        Assert.Contains("BC36943", runner.Console.Error.ToString())

        ' The session survives the failed submissions: the next submission runs.
        Assert.Contains(vbCrLf & "3" & vbCrLf, runner.Console.Out.ToString())
    End Sub

    ''' <summary>
    ''' A script class may span several trees (a '#Load' directive). Each top level statement is checked with the
    ''' binder of its own tree, so one offending 'Await' per file is reported and each one is anchored to its own file
    ''' and line - neither duplicated nor dropped.
    ''' </summary>
    <Fact>
    Public Sub TopLevelAwaitInCatch_AcrossLoadedFiles_IsReportedOncePerTree()
        Dim loadedFile = "C:\scripts\loaded.vbx"
        Dim mainFile = "C:\scripts\main.vbx"
        Dim offendingSource =
            "Try" & vbCrLf &
            "    System.Console.WriteLine(""OK"")" & vbCrLf &
            "Catch ex As System.Exception" & vbCrLf &
            "    Await Task.Delay(1)" & vbCrLf &
            "End Try"
        Dim files = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From
        {
            {loadedFile, offendingSource},
            {mainFile, "#Load ""loaded.vbx""" & vbCrLf & offendingSource}
        }

        Dim options = s_defaultOptions.WithFilePath(mainFile).WithSourceResolver(New InMemorySourceReferenceResolver(files))
        Dim script = VisualBasicScript.Create(files(mainFile), options)

        Dim awaitDiagnostics = script.GetCompilation().GetDiagnostics().Where(Function(d) d.Id = "BC36943").ToArray()

        Dim lineSpans = awaitDiagnostics.Select(Function(d) d.Location.GetLineSpan()).OrderBy(Function(s) s.Path).ToArray()

        Assert.Equal(2, lineSpans.Length)
        Assert.Equal(loadedFile, lineSpans(0).Path)
        Assert.Equal(3, lineSpans(0).StartLinePosition.Line)
        Assert.Equal(mainFile, lineSpans(1).Path)
        Assert.Equal(4, lineSpans(1).StartLinePosition.Line)
    End Sub

    ''' <summary>Resolves '#Load' targets from memory so that no file has to be written.</summary>
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

    Private Const RaiserAndHookupSource As String =
        "Class Raiser" & vbCrLf &
        "    Event SomethingHappened As System.EventHandler" & vbCrLf &
        "    Sub RaiseIt()" & vbCrLf &
        "        RaiseEvent SomethingHappened(Me, System.EventArgs.Empty)" & vbCrLf &
        "    End Sub" & vbCrLf &
        "End Class"

    ''' <summary>
    ''' In-memory console; an existing directory is reused as <c>BuildPaths.TempDir</c> so that nothing is
    ''' written to disk.
    ''' </summary>
    Private Shared Function CreateRunner(input As String) As CommandLineRunner
        Dim buildPaths = New BuildPaths(
            clientDir:=AppContext.BaseDirectory,
            workingDir:=AppContext.BaseDirectory,
            sdkDir:=RuntimeMetadataReferenceResolver.GetDesktopFrameworkDirectory(),
            tempDir:=AppContext.BaseDirectory)
        Dim compiler = New VisualBasicInteractiveCompiler(
            Path.Combine(AppContext.BaseDirectory, "vbi.rsp"), buildPaths, {"/R:System"}, New NotImplementedAnalyzerLoader())
        Return New CommandLineRunner(New TestConsoleIO(input), compiler, VisualBasicScriptCompiler.Instance, VisualBasicObjectFormatter.Instance)
    End Function
End Class
