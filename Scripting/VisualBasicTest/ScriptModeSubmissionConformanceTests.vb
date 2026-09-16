' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System
Imports System.Collections.Generic
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Scripting
Imports Xunit

''' <summary>
''' The interactive half of the matrix (design-detailed.md §U7 已知空白): the submission chain, which the single
''' file probes never reached. Every case runs an in-memory <c>CommandLineRunner</c> session that ends with the
''' marker submission (<c>ScriptModeConformance.ReplMarker</c>), so reaching the marker is the uniform assertion
''' that no submission of the case ended the session.
''' <para>
''' '#Load' inside a console session is not reachable without writing files: the command line compiler builds its
''' own source resolver from the working directory (<c>VisualBasicCompiler.vb:160</c>) and there is no injection
''' point, so the multi tree cases below run through the script API instead (<c>ContinueWith</c> chains with an in
''' memory resolver) - which is a submission chain as well, only without the console loop.
''' </para>
''' </summary>
Public Class ScriptModeSubmissionConformanceTests

#Region "cross submission state and visibility"

    ''' <summary>A field of one submission read and written by the next one.</summary>
    <Fact>
    Public Sub ReplCrossSubmissionFieldAccess_Conforms()
        ScriptModeConformance.AssertReplSession(
            {
                "Dim counter As Integer = 1",
                "counter += 41",
                "? counter"
            },
            "42")
    End Sub

    ''' <summary>The declaration order is the execution order: every submission runs as it arrives.</summary>
    <Fact>
    Public Sub ReplSubmissionExecutionOrder_Conforms()
        ScriptModeConformance.AssertReplSession(
            {
                "Dim order As Integer = 0",
                "order = order * 10 + 1",
                "order = order * 10 + 2",
                "order = order * 10 + 3",
                "? order"
            },
            "123")
    End Sub

    ''' <summary>Imports clauses declared by one submission are seen by the next one.</summary>
    <Fact>
    Public Sub ReplSubmissionImportsAccumulate_Conforms()
        ScriptModeConformance.AssertReplSession(
            {
                "Imports System.Text",
                "Dim builder As New StringBuilder",
                "builder.Append(""imp"")",
                "? builder.Length"
            },
            "3")
    End Sub

    ''' <summary>One type per submission, chained by inheritance across three submissions.</summary>
    <Fact>
    Public Sub ReplTypePerSubmissionChain_Conforms()
        ScriptModeConformance.AssertReplSession(
            {
                "Class Root1",
                "    Public Function Who() As String",
                "        Return ""root""",
                "    End Function",
                "End Class",
                "Class Leaf1",
                "    Inherits Root1",
                "End Class",
                "Class Leaf2",
                "    Inherits Leaf1",
                "End Class",
                "? New Leaf2().Who().Length"
            },
            "4")
    End Sub

    ''' <summary>A nested type declared by one submission and reached from the next one.</summary>
    <Fact>
    Public Sub ReplNestedTypeVisibility_Conforms()
        ScriptModeConformance.AssertReplSession(
            {
                "Class Outer1",
                "    Public Class Inner1",
                "        Public Shared Function Value() As Integer",
                "            Return 9",
                "        End Function",
                "    End Class",
                "End Class",
                "? Outer1.Inner1.Value()"
            },
            "9")
    End Sub

    ''' <summary>A Private member of an earlier submission is not visible to a later one; the session survives it.</summary>
    <Fact>
    Public Sub ReplPrivateMemberOfEarlierSubmission_Conforms()
        ScriptModeConformance.AssertReplSession(
            {
                "Private hidden As Integer = 3",
                "? hidden"
            })
    End Sub

    ''' <summary>A Shared member declared by one submission and used by the next one.</summary>
    <Fact>
    Public Sub ReplSharedMemberAcrossSubmissions_Conforms()
        ScriptModeConformance.AssertReplSession(
            {
                "Shared SharedCounter As Integer = 0",
                "SharedCounter += 5",
                "? SharedCounter"
            },
            "5")
    End Sub

    ''' <summary>Ten submissions, each adding one slot to the submission state.</summary>
    <Fact>
    Public Sub ReplSubmissionSlotResize_Conforms()
        ScriptModeConformance.AssertReplSession(
            {
                "Dim slot0 As Integer = 0",
                "Dim slot1 As Integer = 1",
                "Dim slot2 As Integer = 2",
                "Dim slot3 As Integer = 3",
                "Dim slot4 As Integer = 4",
                "Dim slot5 As Integer = 5",
                "Dim slot6 As Integer = 6",
                "Dim slot7 As Integer = 7",
                "Dim slot8 As Integer = 8",
                "Dim slot9 As Integer = 9",
                "? slot0 + slot9"
            },
            "9")
    End Sub

    ''' <summary>A generic type declared by one submission, instantiated by the next one.</summary>
    <Fact>
    Public Sub ReplGenericTypeAcrossSubmissions_Conforms()
        ScriptModeConformance.AssertReplSession(
            {
                "Class Box2(Of T)",
                "    Public Item As T",
                "End Class",
                "Dim madeBox2 As New Box2(Of Integer)",
                "madeBox2.Item = 5",
                "? madeBox2.Item"
            },
            "5")
    End Sub

    ''' <summary>A method pointer taken from an instance of an earlier submission.</summary>
    <Fact>
    Public Sub ReplDelegateFromEarlierSubmission_Conforms()
        ScriptModeConformance.AssertReplSession(
            {
                "Class Target1",
                "    Public Hits As Integer = 0",
                "    Public Sub Hit()",
                "        Hits += 1",
                "    End Sub",
                "End Class",
                "Dim madeTarget As New Target1",
                "Dim act As System.Action = AddressOf madeTarget.Hit",
                "act()",
                "? madeTarget.Hits"
            },
            "1")
    End Sub

    ''' <summary>An override declared by an earlier submission is what the later one calls.</summary>
    <Fact>
    Public Sub ReplObjectOverrideAcrossSubmissions_Conforms()
        ScriptModeConformance.AssertReplSession(
            {
                "Class Printable1",
                "    Public Overrides Function ToString() As String",
                "        Return ""PRINTED""",
                "    End Function",
                "End Class",
                "Dim madePrintable As New Printable1",
                "? madePrintable.ToString().Length"
            },
            "7")
    End Sub

    ''' <summary>
    ''' Two structurally identical anonymous types, one per submission: the Key based structural equality of VB
    ''' anonymous types decides whether the submissions share one type.
    ''' </summary>
    <Fact>
    Public Sub ReplAnonymousTypesAcrossSubmissions_Conform()
        ScriptModeConformance.AssertReplSession(
            {
                "Dim firstAnon As Object = New With {Key .Id = 1}",
                "Dim secondAnon As Object = New With {Key .Id = 1}",
                "? firstAnon.Equals(secondAnon)"
            },
            "True")
    End Sub

    ''' <summary>A type redefined by a later submission: either it shadows or it is reported, but the session
    ''' survives.</summary>
    <Fact>
    Public Sub ReplTypeRedefinition_Conforms()
        ScriptModeConformance.AssertReplSession(
            {
                "Class Same1",
                "End Class",
                "Class Same1",
                "End Class",
                "? New Same1() IsNot Nothing"
            },
            "True")
    End Sub

#End Region

#Region "cross submission events and exceptions"

    ''' <summary>
    ''' A WithEvents field declared by one submission, and AddHandler / RaiseIt in later ones. The cross submission
    ''' Handles clause is a reported diagnostic (ScriptTopLevelCrashTests.ReplCrossSubmissionHandles_*, BC37343);
    ''' this is the registration path that still has to deliver.
    ''' </summary>
    <Fact>
    Public Sub ReplWithEventsAndAddHandlerAcrossSubmissions_Conform()
        ScriptModeConformance.AssertReplSession(
            {
                "Class Raiser2",
                "    Public Event SomethingHappened As System.EventHandler",
                "    Public Sub RaiseIt()",
                "        RaiseEvent SomethingHappened(Me, System.EventArgs.Empty)",
                "    End Sub",
                "End Class",
                "WithEvents hooked2 As New Raiser2",
                "Dim handlerCount As Integer = 0 : AddHandler hooked2.SomethingHappened, Sub(s As Object, e As System.EventArgs) handlerCount += 1",
                "hooked2.RaiseIt()",
                "? handlerCount"
            },
            "1")
    End Sub

    ''' <summary>An uncaught exception in a submission is reported and the session continues.</summary>
    <Fact>
    Public Sub ReplUncaughtException_KeepsTheSessionAlive()
        ScriptModeConformance.AssertReplSession(
            {
                "Throw New System.InvalidOperationException(""boom"")",
                "? 1 + 2"
            },
            "3")
    End Sub

    ''' <summary>An exception caught by the submission itself does not disturb the session.</summary>
    <Fact>
    Public Sub ReplHandledException_KeepsTheSessionAlive()
        ScriptModeConformance.AssertReplSession(
            {
                "Dim caughtCount As Integer = 0",
                "Try",
                "    Throw New System.InvalidOperationException(""boom"")",
                "Catch ex As System.Exception",
                "    caughtCount = 1",
                "End Try",
                "? caughtCount"
            },
            "1")
    End Sub

    ''' <summary>
    ''' The REPL face of <c>PreservingDeclarationsOnException</c> (roslyn
    ''' <c>src\Scripting\CSharpTest\CommandLineRunnerTests.cs:848</c> attribute line, <c>PreservingDeclarationsOnException</c>
    ''' declared at <c>:850</c>; the submission chain dual is <c>InteractiveSessionTests.cs:1919</c>,
    ''' <c>PreservingDeclarationsOnException1</c>): the declaration made by a submission <em>before</em> the throwing one
    ''' is still readable after the session reports the exception. <c>ReplUncaughtException_KeepsTheSessionAlive</c> above
    ''' is the weaker half (session survives); this cell pins the value, so an implementation that discarded the earlier
    ''' submission state would fail here instead of passing on the marker alone.
    ''' </summary>
    <Fact>
    Public Sub ReplUncaughtException_PreservesEarlierSubmissionDeclaration()
        ScriptModeConformance.AssertReplSession(
            {
                "Dim kept As Integer = 7",
                "Throw New System.InvalidOperationException(""boom"")",
                "? kept"
            },
            "7")
    End Sub

    ''' <summary>
    ''' The same shape as the C# REPL cell, statement for statement (<c>CommandLineRunnerTests.cs:850</c>: <c>i</c> alone,
    ''' then <c>j</c> + throw + <c>k</c> in one submission, then <c>i + j + k</c>). Expected 120 is the C# baseline's
    ''' 120: the throwing submission's own residual <c>k</c> survives as a declaration but its initializer never ran.
    ''' The cell can fail two ways - a rolled back throwing submission does not resolve <c>j</c>/<c>k</c>, and a host that
    ''' re-ran the pending initializer answers 123.
    ''' </summary>
    <Fact>
    Public Sub ReplUncaughtException_KeepsTheThrowingSubmissionsOwnDeclaration()
        ScriptModeConformance.AssertReplSession(
            {
                "Dim i As Integer = 100",
                "Dim j As Integer = 20 : Throw New System.InvalidOperationException(""Bang!"") : Dim k As Integer = 3",
                "? i + j + k"
            },
            "120")
    End Sub

    ''' <summary>An Await in a submission: the interactive host compiles the submission as an async method.</summary>
    <Fact>
    Public Sub ReplAwaitInSubmission_Conforms()
        ScriptModeConformance.AssertReplSession(
            {
                "Dim pending = System.Threading.Tasks.Task.FromResult(7)",
                "? Await pending"
            },
            "7")
    End Sub

#End Region

#Region "exception and cancellation chains"

    ' The cells below are the VB duals of the seven C# baseline cells
    '   PreservingDeclarationsOnException1-4 (roslyn src\Scripting\CSharpTest\InteractiveSessionTests.cs:1919,1942,1969,1997)
    '   PreservingDeclarationsOnCancellation1-3 (same file :2025,2059,2093)
    ' and they run through the same mechanism the C# baseline relies on: when catchException accepts the exception, the
    ' submissions that did not run - and the current one - are instantiated without executing user code
    ' (Scripting\Core\ScriptExecutionState.cs:125-151), so their declarations exist in the chain. A continuation from that
    ' state reuses those instances instead of re-running the chain (Script.cs:536-545, TryGetPrecedingExecutors walks back
    ' only as far as the state's own submission, and FreezeAndClone keeps the instances), which is why the never executed
    ' submissions answer with default values and not with their initializers.
    ' The VB differences from the C# text are two, both structural:
    '   - VB has no local functions, so the C# "int F() => i + j" is a top level Function of a submission.
    '   - a typed submission follows Function Main semantics, so the result comes from "Return", not from a trailing
    '     expression (Analysis\InitializerRewriter.vb:206-207 in the fork).
    ' No cell uses a timer, a sleep or a delay: the cancellation cells cancel the token from inside the script, and the
    ' token is only observed at submission boundaries (ScriptExecutionState.cs:90 before each preceding executor and
    ' :106 before the current one), so the outcome is decided by submission order alone.

    ''' <summary>Runs a chain to its end with the C# baseline's <c>catchException: e =&gt; true</c> and returns the state
    ''' the exception was stored on.</summary>
    Private Shared Function RunCatching(script As Script(Of Object), globals As Object, token As CancellationToken) As ScriptState(Of Object)
        Return script.RunAsync(globals, Function(e As Exception) True, token).GetAwaiter().GetResult()
    End Function

    ''' <summary>
    ''' Dual of <c>PreservingDeclarationsOnException1</c> (<c>InteractiveSessionTests.cs:1919</c>). The first submission
    ''' initialises <c>i</c>, throws, and declares <c>j</c> after the throw; the continuation declares <c>Total</c> over
    ''' both names. Expectation 10 is the C# baseline's 10 - <c>i</c> plus the never initialised <c>j</c>. The cell can
    ''' fail two ways: an implementation that rolled the throwing submission back leaves <c>Total</c> unbound (the
    ''' continuation raises CompilationErrorException, not 10), and one that re-ran the pending initializers after the
    ''' throw would answer 12.
    ''' </summary>
    <Fact>
    Public Sub ExceptionSubmission_DeclarationsAroundTheThrow_ArePreserved()
        Dim first = VisualBasicScript.Create(
            "Dim i As Integer = 10" & vbCrLf &
            "Throw New System.Exception(""Bang!"")" & vbCrLf &
            "Dim j As Integer = 2",
            ScriptModeConformance.DefaultOptions)

        Dim continuation = first.ContinueWith(
            "Function Total() As Integer" & vbCrLf &
            "    Return i + j" & vbCrLf &
            "End Function")

        Dim caught = RunCatching(continuation, Nothing, Nothing)
        Dim thrown = Assert.IsType(Of Exception)(caught.Exception)
        Assert.Equal("Bang!", thrown.Message)

        Dim resumed = caught.ContinueWithAsync(Of Integer)("Return Total()").GetAwaiter().GetResult()
        Assert.Equal(10, resumed.ReturnValue)
    End Sub

    ''' <summary>
    ''' The falsifiability witness of the preservation cells: the same first submission as the cell above with <c>j</c>
    ''' left out, and the same continuation over <c>i + j</c>. It is rejected where the full shape returned 10, so the
    ''' number in the cell above is only meaningful because the continuation really does need the declaration. Without
    ''' this control "the continuation returned 10" could not be told apart from a continuation that never resolved
    ''' <c>j</c> at all.
    ''' </summary>
    <Fact>
    Public Sub ContinuationWithoutTheDeclaration_IsRejected()
        Dim first = VisualBasicScript.Create(
            "Dim i As Integer = 10" & vbCrLf &
            "Throw New System.Exception(""Bang!"")",
            ScriptModeConformance.DefaultOptions)
        Dim continuation = first.ContinueWith(
            "Function Total() As Integer" & vbCrLf &
            "    Return i + j" & vbCrLf &
            "End Function")

        Dim ids = continuation.Compile().Select(Function(d) d.Id).ToArray()
        Assert.True(ids.Contains("BC30451"),
                    "expected BC30451 for the missing declaration, got: " & String.Join(", ", ids))
    End Sub

    ''' <summary>
    ''' Dual of <c>PreservingDeclarationsOnException2</c> (<c>InteractiveSessionTests.cs:1942</c>): the throw is in the
    ''' middle submission, whose own <c>j</c> was initialised before it and whose <c>k</c> comes after it. Expectation
    ''' 120 distinguishes the three declarations (100 + 20 + 0); a chain that lost the throwing submission would not
    ''' bind <c>j</c> or <c>k</c> at all, and one that re-ran the pending initializer would answer 123.
    ''' </summary>
    <Fact>
    Public Sub ExceptionInMiddleSubmission_KeepsDeclarationsOnBothSidesOfTheThrow()
        Dim first = VisualBasicScript.Create("Dim i As Integer = 100", ScriptModeConformance.DefaultOptions)
        Dim second = first.ContinueWith(
            "Dim j As Integer = 20" & vbCrLf &
            "Throw New System.Exception(""Bang!"")" & vbCrLf &
            "Dim k As Integer = 3")
        Dim third = second.ContinueWith(
            "Function Total() As Integer" & vbCrLf &
            "    Return i + j + k" & vbCrLf &
            "End Function")

        Dim caught = RunCatching(third, Nothing, Nothing)
        Dim thrown = Assert.IsType(Of Exception)(caught.Exception)
        Assert.Equal("Bang!", thrown.Message)

        Dim resumed = caught.ContinueWithAsync(Of Integer)("Return Total()").GetAwaiter().GetResult()
        Assert.Equal(120, resumed.ReturnValue)
    End Sub

    ''' <summary>
    ''' Dual of <c>PreservingDeclarationsOnException3</c> (<c>InteractiveSessionTests.cs:1969</c>): the submission that
    ''' is never reached at all (the one after the throwing submission) also keeps its declaration, because the catch
    ''' path instantiates it without running its code (ScriptExecutionState.cs:131-140). Expectation 1200 = 1000 + 200
    ''' + 0 + 0; a chain that dropped the unreached submission would fail to bind <c>l</c>.
    ''' </summary>
    <Fact>
    Public Sub ExceptionSubmission_UnreachedSuccessorDeclaration_IsPreserved()
        Dim first = VisualBasicScript.Create("Dim i As Integer = 1000", ScriptModeConformance.DefaultOptions)
        Dim second = first.ContinueWith(
            "Dim j As Integer = 200" & vbCrLf &
            "Throw New System.Exception(""Bang!"")" & vbCrLf &
            "Dim k As Integer = 30")
        Dim third = second.ContinueWith("Dim l As Integer = 4")
        Dim fourth = third.ContinueWith(
            "Function Total() As Integer" & vbCrLf &
            "    Return i + j + k + l" & vbCrLf &
            "End Function")

        Dim caught = RunCatching(fourth, Nothing, Nothing)
        Dim thrown = Assert.IsType(Of Exception)(caught.Exception)
        Assert.Equal("Bang!", thrown.Message)

        Dim resumed = caught.ContinueWithAsync(Of Integer)("Return Total()").GetAwaiter().GetResult()
        Assert.Equal(1200, resumed.ReturnValue)
    End Sub

    ''' <summary>
    ''' Dual of <c>PreservingDeclarationsOnException4</c> (<c>InteractiveSessionTests.cs:1997</c>): two consecutive
    ''' throwing submissions, the second one declaring <c>l</c> before it throws. This is the cell that separates
    ''' "declaration exists" from "initializer ran": with <c>i</c> = 1000 and <c>j</c> = 200 fixed, the four reachable
    ''' sums are 1200 (<c>k</c> and <c>l</c> both uninitialised), 1230 (<c>k</c> ran), 1204 (<c>l</c> ran), 1234 (both
    ''' ran). 1204 is the C# baseline's 1204, so an implementation that keeps the second throwing submission's declaration
    ''' but not the user code that initialises <c>l</c> - its instance rebuilt as the catch path rebuilds submissions that
    ''' never ran (the guard at ScriptExecutionState.cs:142-151 not holding), instead of the instance its own user code
    ''' already ran on - answers 1200, and one that re-ran the initializer of the never reached <c>k</c> answers 1230.
    ''' </summary>
    <Fact>
    Public Sub TwoConsecutiveExceptionSubmissions_KeepEveryDeclaration()
        Dim state0 = RunCatching(
            VisualBasicScript.Create("Dim i As Integer = 1000", ScriptModeConformance.DefaultOptions), Nothing, Nothing)

        Dim state1 = state0.ContinueWithAsync(
            "Dim j As Integer = 200" & vbCrLf &
            "Throw New System.Exception(""Bang 1!"")" & vbCrLf &
            "Dim k As Integer = 30",
            catchException:=Function(e As Exception) True).GetAwaiter().GetResult()
        Dim thrown1 = Assert.IsType(Of Exception)(state1.Exception)
        Assert.Equal("Bang 1!", thrown1.Message)

        Dim state2 = state1.ContinueWithAsync(Of Integer)(
            "Dim l As Integer = 4" & vbCrLf &
            "Throw New System.Exception(""Bang 2!"")" & vbCrLf &
            "Return 1",
            catchException:=Function(e As Exception) True).GetAwaiter().GetResult()
        Dim thrown2 = Assert.IsType(Of Exception)(state2.Exception)
        Assert.Equal("Bang 2!", thrown2.Message)

        Dim resumed = state2.ContinueWithAsync("Return i + j + k + l").GetAwaiter().GetResult()
        Assert.Equal(1204, CInt(resumed.ReturnValue))
    End Sub

    ''' <summary>
    ''' The rejecting half of the <c>catchException</c> contract (the C# baseline only exercises the accepting filter,
    ''' <c>InteractiveSessionTests.cs:1931</c>). Same chain and same source as
    ''' <c>ExceptionSubmission_DeclarationsAroundTheThrow_ArePreserved</c>, opposite filter, opposite outcome: the
    ''' exception has to escape <c>RunAsync</c> rather than land on the state. The pair is what makes both cells
    ''' falsifiable - a host that always captured would fail here, a host that never captured would fail there.
    ''' </summary>
    <Fact>
    Public Sub ExceptionSubmission_CatchFilterRejects_PropagatesOutOfRunAsync()
        Dim first = VisualBasicScript.Create(
            "Dim i As Integer = 10" & vbCrLf &
            "Throw New System.Exception(""Bang!"")",
            ScriptModeConformance.DefaultOptions)

        Dim thrown = Assert.Throws(Of Exception)(
            Sub() first.RunAsync(Nothing, Function(e As Exception) False).GetAwaiter().GetResult())
        Assert.Equal("Bang!", thrown.Message)
    End Sub

    ''' <summary>
    ''' The globals of the cancellation cells. The script cancels its own token from inside the chain, exactly as the C#
    ''' baseline does (<c>InteractiveSessionTests.cs:2038</c>, <c>Value.Cancel()</c> in a submission of the chain, with
    ''' the <c>StrongBox</c> globals built at <c>:2029-2030</c>), which keeps the trigger off any clock.
    ''' </summary>
    Public Class CancellationGlobals
        Public ReadOnly TokenSource As CancellationTokenSource

        ' The parameter is deliberately not named 'tokenSource': VB is case insensitive, so that name would shadow the
        ' field and turn the assignment into a self assignment, leaving the field Nothing.
        Public Sub New(cancellationSource As CancellationTokenSource)
            TokenSource = cancellationSource
        End Sub
    End Class

    ''' <summary>
    ''' Dual of <c>PreservingDeclarationsOnCancellation1</c> (<c>InteractiveSessionTests.cs:2025</c>). The second
    ''' submission cancels the token; the third is never executed because the token is observed at the submission
    ''' boundary (ScriptExecutionState.cs:90) and the catch path only constructs it (ScriptExecutionState.cs:131-140).
    ''' Expectation 1230 is the C# baseline's 1230: continuing from the state the cancellation was stored on reuses the
    ''' frozen submission instances (<c>Script.cs:536-545</c>, <c>TryGetPrecedingExecutors</c> + <c>FreezeAndClone</c>),
    ''' so <c>l</c> stays at its default while <c>i</c>, <c>j</c> and <c>k</c> keep the values of the cancelled run. A
    ''' chain that discarded the cancelled part would not bind <c>Total</c> at all, and one that re-ran the never
    ''' reached submission would answer 1234.
    ''' </summary>
    <Fact>
    Public Sub CancelledChain_DeclarationsOfEverySubmission_ArePreserved()
        Dim source = New CancellationTokenSource()
        Dim fourth = BuildCancellationChain(source, cancelInSubmission:=2)

        Dim caught = RunCatching(fourth, New CancellationGlobals(source), source.Token)
        Assert.IsType(Of OperationCanceledException)(caught.Exception)

        Dim resumed = caught.ContinueWithAsync(Of Integer)("Return Total()").GetAwaiter().GetResult()
        Assert.Equal(1230, resumed.ReturnValue)
    End Sub

    ''' <summary>
    ''' Dual of <c>PreservingDeclarationsOnCancellation2</c> (<c>InteractiveSessionTests.cs:2059</c>): the cancelling
    ''' submission declares <c>l</c> just before it cancels, so the cancelled submission is the third one and its own
    ''' declaration is the one at stake. 1234 (not 1230) pins that <c>l</c> was declared and initialised before the
    ''' cancellation was observed.
    ''' </summary>
    <Fact>
    Public Sub CancelledSubmission_OwnDeclaration_IsPreserved()
        Dim source = New CancellationTokenSource()
        Dim fourth = BuildCancellationChain(source, cancelInSubmission:=3)

        Dim caught = RunCatching(fourth, New CancellationGlobals(source), source.Token)
        Assert.IsType(Of OperationCanceledException)(caught.Exception)

        Dim resumed = caught.ContinueWithAsync(Of Integer)("Return Total()").GetAwaiter().GetResult()
        Assert.Equal(1234, resumed.ReturnValue)
    End Sub

    ''' <summary>
    ''' Dual of <c>PreservingDeclarationsOnCancellation3</c> (<c>InteractiveSessionTests.cs:2093</c>): a filter that
    ''' rejects <see cref="OperationCanceledException"/> turns the same cancellation into a propagating exception, so
    ''' the cancellation is not silently absorbed. Together with the two cells above this pins both directions of the
    ''' filter for cancellation, not just the accepting one.
    ''' </summary>
    <Fact>
    Public Sub CancelledChain_RejectingCatchFilter_PropagatesOperationCanceled()
        Dim source = New CancellationTokenSource()
        Dim fourth = BuildCancellationChain(source, cancelInSubmission:=2)

        Assert.Throws(Of OperationCanceledException)(
            Sub() fourth.RunAsync(
                     New CancellationGlobals(source),
                     Function(e As Exception) TypeOf e IsNot OperationCanceledException,
                     source.Token).GetAwaiter().GetResult())
    End Sub

    ''' <summary>
    ''' The chain of the three cancellation cells, the same five submissions in both: <c>i = 1000</c>, <c>j = 200</c>
    ''' (+ <c>TokenSource.Cancel()</c> when <paramref name="cancelInSubmission"/> is 2), <c>k = 30</c>, then <c>l = 4</c>
    ''' (+ <c>TokenSource.Cancel()</c> when it is 3), then the <c>Total</c> declaration that ties all four names
    ''' together. Only the cancellation point moves between the cells, so their expected sums differ by exactly the
    ''' submission that the cancelled run did or did not reach: with the cancel at 2 the never reached <c>l</c> keeps
    ''' the default 0 (1230), with it at 3 <c>l</c> was already initialised (1234).
    ''' </summary>
    Private Shared Function BuildCancellationChain(source As CancellationTokenSource, cancelInSubmission As Integer) As Script(Of Object)
        Dim options = ScriptModeConformance.DefaultOptions
        Dim first = VisualBasicScript.Create("Dim i As Integer = 1000", options, GetType(CancellationGlobals))
        Dim second = first.ContinueWith(
            "Dim j As Integer = 200" & vbCrLf &
            If(cancelInSubmission = 2, "TokenSource.Cancel()" & vbCrLf, "") &
            "Dim k As Integer = 30")
        Dim third = second.ContinueWith(
            "Dim l As Integer = 4" & vbCrLf &
            If(cancelInSubmission = 3, "TokenSource.Cancel()" & vbCrLf, ""))
        Return third.ContinueWith(
            "Function Total() As Integer" & vbCrLf &
            "    Return i + j + k + l" & vbCrLf &
            "End Function")
    End Function

#End Region

#Region "multi tree submissions"

    ''' <summary>
    ''' A loaded tree in a later submission: the helper declared by the loaded file and the field declared by the
    ''' earlier submission are both reachable from the same top level statement.
    ''' </summary>
    <Fact>
    Public Sub LoadedTreeInLaterSubmission_Conforms()
        Dim mainFile = "C:\scripts\main.vbx"
        Dim files = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From
        {
            {mainFile, "#Load ""loaded6.vbx""" & vbCrLf & "Return seed + LoadedHelper()"},
            {"C:\scripts\loaded6.vbx", "Function LoadedHelper() As Integer" & vbCrLf & "    Return 5" & vbCrLf & "End Function"}
        }

        Assert.Equal(15, ScriptModeConformance.RunChain(files, mainFile,
            "Dim seed As Integer = 10",
            files(mainFile)))
    End Sub

    ''' <summary>Two loaded trees in one submission, each contributing a method.</summary>
    <Fact>
    Public Sub LoadedTreesMultiplePerSubmission_Conform()
        Dim mainFile = "C:\scripts\main.vbx"
        Dim files = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From
        {
            {mainFile, "#Load ""loaded7a.vbx""" & vbCrLf & "#Load ""loaded7b.vbx""" & vbCrLf & "Return FromA() * FromB()"},
            {"C:\scripts\loaded7a.vbx", "Function FromA() As Integer" & vbCrLf & "    Return 2" & vbCrLf & "End Function"},
            {"C:\scripts\loaded7b.vbx", "Function FromB() As Integer" & vbCrLf & "    Return 3" & vbCrLf & "End Function"}
        }

        Assert.Equal(6, ScriptModeConformance.RunChain(files, mainFile, files(mainFile)))
    End Sub

    ''' <summary>
    ''' The combination: a loaded tree whose own top level code reaches a member of the earlier submission. The
    ''' loaded file is a tree of its own, so this is the per tree binder case on the chain path.
    ''' </summary>
    <Fact>
    Public Sub LoadedTreeReachingEarlierSubmission_Conforms()
        Dim mainFile = "C:\scripts\main.vbx"
        Dim files = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From
        {
            {mainFile, "#Load ""loaded8.vbx""" & vbCrLf & "Return Doubled()"},
            {"C:\scripts\loaded8.vbx", "Function Doubled() As Integer" & vbCrLf & "    Return seed * 2" & vbCrLf & "End Function"}
        }

        Assert.Equal(8, ScriptModeConformance.RunChain(files, mainFile,
            "Dim seed As Integer = 4",
            files(mainFile)))
    End Sub

#End Region

#Region "nested lambdas"

    ''' <summary>
    ''' Three levels of nesting with Async, Iterator, Await and Yield interleaved: an async lambda holds an
    ''' iterator lambda, which holds a plain lambda, and the async one awaits after consuming the iterator.
    ''' </summary>
    <Fact>
    Public Sub ThreeLevelNestedLambdasAwaitAndYield_Conform()
        ScriptModeConformance.AssertRuns(
            "Dim levels As New System.Collections.Generic.List(Of String)" & vbCrLf &
            "Dim outer As System.Func(Of System.Threading.Tasks.Task) = Async Function()" & vbCrLf &
            "    levels.Add(""o"")" & vbCrLf &
            "    Await Task.Delay(1)" & vbCrLf &
            "    Dim mid As System.Func(Of System.Collections.Generic.IEnumerable(Of Integer)) = Iterator Function()" & vbCrLf &
            "        levels.Add(""m"")" & vbCrLf &
            "        Yield 1" & vbCrLf &
            "        Dim inner As System.Action = Sub()" & vbCrLf &
            "            levels.Add(""i"")" & vbCrLf &
            "        End Sub" & vbCrLf &
            "        inner()" & vbCrLf &
            "        Yield 2" & vbCrLf &
            "    End Function" & vbCrLf &
            "    For Each value In mid()" & vbCrLf &
            "        levels.Add(value.ToString())" & vbCrLf &
            "    Next" & vbCrLf &
            "End Function" & vbCrLf &
            "outer().Wait()" & vbCrLf &
            "Return String.Join("","", levels)", "o,m,1,i,2")
    End Sub

    ''' <summary>
    ''' The three level nesting on the error path: the innermost lambda is not an async context, so the Await is
    ''' reported instead of terminating the process.
    ''' </summary>
    <Fact>
    Public Sub ThreeLevelNestedLambdaWithAwaitInNonAsyncContext_IsReported()
        ScriptModeConformance.AssertReports(
            "Dim outer As System.Action = Sub()" & vbCrLf &
            "    Dim mid As System.Action = Sub()" & vbCrLf &
            "        Dim inner As System.Action = Sub()" & vbCrLf &
            "            Await Task.Delay(1)" & vbCrLf &
            "        End Sub" & vbCrLf &
            "        inner()" & vbCrLf &
            "    End Sub" & vbCrLf &
            "    mid()" & vbCrLf &
            "End Sub" & vbCrLf &
            "outer()", "BC30800")
    End Sub

    ''' <summary>An async lambda nested in a lambda nested in an iterator method of the submission class.</summary>
    <Fact>
    Public Sub NestedLambdasInsideIteratorMethod_Conform()
        ScriptModeConformance.AssertRuns(
            "Dim log As String = """"" & vbCrLf &
            "Iterator Function Walk() As System.Collections.Generic.IEnumerable(Of Integer)" & vbCrLf &
            "    Dim step1 As System.Func(Of Integer, System.Threading.Tasks.Task(Of Integer)) = Async Function(x)" & vbCrLf &
            "                                                                                                    Await Task.Delay(1)" & vbCrLf &
            "                                                                                                    Return x + 1" & vbCrLf &
            "                                                                                                End Function" & vbCrLf &
            "    For i = 1 To 2" & vbCrLf &
            "        log &= step1(i).Result" & vbCrLf &
            "        Dim step2 As System.Action = Sub() log &= ""!""" & vbCrLf &
            "        step2()" & vbCrLf &
            "        Yield i" & vbCrLf &
            "    Next" & vbCrLf &
            "End Function" & vbCrLf &
            "For Each value In Walk()" & vbCrLf &
            "    log &= value" & vbCrLf &
            "Next" & vbCrLf &
            "Return log", "2!13!2")
    End Sub

#End Region

#Region "interop shapes"

    ''' <summary>Declare with Alias, Auto, Ansi and Unicode. The declared functions are never called.</summary>
    <Fact>
    Public Sub TopLevelDeclareForms_Conform()
        ScriptModeConformance.AssertRuns(
            "Declare Function GetTickCount1 Lib ""kernel32"" Alias ""GetTickCount"" () As UInteger" & vbCrLf &
            "Declare Auto Function MessageBoxText Lib ""user32"" (h As System.IntPtr, t As String, c As String, u As UInteger) As Integer" & vbCrLf &
            "Declare Ansi Function LengthA Lib ""kernel32"" (s As String) As Integer" & vbCrLf &
            "Declare Unicode Function LengthW Lib ""kernel32"" (s As String) As Integer" & vbCrLf &
            "Return ""DECLARED""", "DECLARED")
    End Sub

    ''' <summary>A DllImport method declared by the submission class (never called).</summary>
    <Fact>
    Public Sub TopLevelDllImportMethod_Conform()
        ScriptModeConformance.AssertRuns(
            "Imports System.Runtime.InteropServices" & vbCrLf &
            "<DllImport(""kernel32"")>" & vbCrLf &
            "Shared Function Tick() As UInteger" & vbCrLf &
            "End Function" & vbCrLf &
            "Return ""DLLIMPORT""", "DLLIMPORT")
    End Sub

    ''' <summary>MarshalAs on Declare parameters, including an array and a ByRef out parameter.</summary>
    <Fact>
    Public Sub TopLevelDeclareMarshalAs_Conform()
        ScriptModeConformance.AssertRuns(
            "Imports System.Runtime.InteropServices" & vbCrLf &
            "Declare Sub TakesArray Lib ""kernel32"" (<MarshalAs(UnmanagedType.LPArray)> data() As Integer)" & vbCrLf &
            "Declare Function PreservedResult Lib ""kernel32"" (<MarshalAs(UnmanagedType.Bool)> flag As Boolean) As Integer" & vbCrLf &
            "Declare Function ByRefOut Lib ""kernel32"" (<Out> ByRef value As Integer) As Integer" & vbCrLf &
            "Return ""MARSHAL""", "MARSHAL")
    End Sub

    ''' <summary>A ComImport interface with Guid and PreserveSig, declared by the submission class.</summary>
    <Fact>
    Public Sub NestedComImportInterface_Conform()
        ScriptModeConformance.AssertRuns(
            "Imports System.Runtime.InteropServices" & vbCrLf &
            "<ComImport>" & vbCrLf &
            "<Guid(""00000000-0000-0000-C000-000000000046"")>" & vbCrLf &
            "Interface IUnknownLike" & vbCrLf &
            "    <PreserveSig>" & vbCrLf &
            "    Function QuerySomething() As Integer" & vbCrLf &
            "End Interface" & vbCrLf &
            "Return GetType(IUnknownLike).Name", "IUnknownLike")
    End Sub

    ''' <summary>
    ''' ComClass is rejected in script mode: the attribute needs a public container and the submission class is not
    ''' public (BC32504). The cell is a diagnostic, not a crash.
    ''' </summary>
    <Fact>
    Public Sub NestedComClass_IsReported()
        ScriptModeConformance.AssertReports(
            "Imports Microsoft.VisualBasic" & vbCrLf &
            "Imports System.Runtime.InteropServices" & vbCrLf &
            "<ComVisible(True)>" & vbCrLf &
            "<ComClass(""8D20E0F8-1111-2222-3333-444455556666"", ""1B2C3D4E-2222-3333-4444-555566667777"", ""C0FFEE00-3333-4444-5555-666677778888"")>" & vbCrLf &
            "Public Class ComThing" & vbCrLf &
            "    Public Sub New()" & vbCrLf &
            "    End Sub" & vbCrLf &
            "End Class" & vbCrLf &
            "Return ""COM""", "BC32504")
    End Sub

#End Region

End Class
