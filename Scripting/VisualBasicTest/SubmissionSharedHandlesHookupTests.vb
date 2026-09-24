' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System
Imports System.Collections.Generic
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting
Imports Xunit

''' <summary>
''' The runtime half of the shared <c>Handles</c> hookup of the submission class
''' (<c>InternalDevDocs\tasks\submission-shared-handles-hookup\test-plan.md</c> T2, T4, T5, T6, R1, R2).
''' <para>
''' A <c>Handles</c> clause whose method and event are both shared is hooked up in the shared constructor of the
''' class that declares the method, and a submission class is a class container. It used to be given a shared
''' constructor only when it had shared initializers, so the clause had no host. Every case below counts deliveries
''' rather than looking for output: the counter lives in a class the script itself declares, so reading it does not
''' initialize the submission class and a delivery can never be an artifact of the observation.
''' </para>
''' <para>
''' The raise always goes through a shared method of the class that owns the event: a top-level <c>RaiseEvent</c>
''' statement is not a legal submission statement (<c>BC30188</c>), which is why the counter has to be handed over
''' by a nested class or by a shared <c>Fire</c>.
''' </para>
''' </summary>
Public Class SubmissionSharedHandlesHookupTests

    ''' <summary>The counter of every case below. No shared initializer, so <c>Sink</c> needs no type run to be read.</summary>
    Private Const SinkSource As String =
        "Class Sink" & vbCrLf &
        "    Public Shared N As Integer" & vbCrLf &
        "    Public Shared Sub Mark()" & vbCrLf &
        "        N = N + 1" & vbCrLf &
        "    End Sub" & vbCrLf &
        "    Public Shared Sub MarkBy(amount As Integer)" & vbCrLf &
        "        N = N + amount" & vbCrLf &
        "    End Sub" & vbCrLf &
        "    Public Shared Function Count() As Integer" & vbCrLf &
        "        Return N" & vbCrLf &
        "    End Function" & vbCrLf &
        "    Public Shared Sub Reset()" & vbCrLf &
        "        N = 0" & vbCrLf &
        "    End Sub" & vbCrLf &
        "End Class" & vbCrLf

    ''' <summary>
    ''' A shared event of the submission class, a shared handler of it, and a shared raiser - and no shared
    ''' initializer anywhere. The handler is one mark per delivery.
    ''' </summary>
    Private Shared Function HookupSource(container As String, raises As Integer) As String
        Dim source = New System.Text.StringBuilder()
        source.AppendLine(SinkSource)
        source.AppendLine("Shared Event Ev As System.EventHandler")
        source.AppendLine("Shared Sub H(s As Object, e As System.EventArgs) Handles " & container & "Ev")
        source.AppendLine("    Sink.Mark()")
        source.AppendLine("End Sub")
        source.AppendLine("Shared Sub Fire()")
        source.AppendLine("    RaiseEvent Ev(Nothing, System.EventArgs.Empty)")
        source.AppendLine("End Sub")
        For index = 1 To raises
            source.AppendLine("Fire()")
        Next
        source.AppendLine("Return Sink.Count()")
        Return source.ToString()
    End Function

#Region "T2 the shared hookup of the submission class delivers"

    ''' <summary>
    ''' The shape of the defect: one raise, exactly one delivery. Before the host was synthesized this submission
    ''' never even compiled (<c>IndexOutOfRangeException</c> out of <c>SharedConstructors(0)</c>).
    ''' </summary>
    <Fact>
    Public Sub SharedHandlesInSubmission_DeliversExactlyOnce()
        ScriptModeConformance.AssertRuns(HookupSource("MyClass.", raises:=1), 1)
    End Sub

    ''' <summary>
    ''' Two raises, two deliveries: the clause is hooked up once, not once per raise and not zero times.
    ''' </summary>
    <Fact>
    Public Sub SharedHandlesInSubmission_EveryRaiseDeliversOnce()
        ScriptModeConformance.AssertRuns(HookupSource("MyClass.", raises:=2), 2)
    End Sub

    ''' <summary><c>Handles Me.Ev</c> names the same event of the same class and gets the same delivery.</summary>
    <Fact>
    Public Sub SharedHandlesWithMeKeyword_DeliversExactlyOnce()
        ScriptModeConformance.AssertRuns(HookupSource("Me.", raises:=1), 1)
    End Sub

#End Region

#Region "T4 / T5 the shared initializer keeps its own timing"

    ''' <summary>
    ''' The laziness lock (the half of the issue that is C# parity and is deliberately not changed): a shared field
    ''' initializer without any hookup still runs when the type is first initialized, not when the script starts.
    ''' Nothing in the submission reads <c>a</c>, so the mark made by its initializer must not have happened yet
    ''' when the last statement runs.
    ''' </summary>
    <Fact>
    Public Sub SharedFieldInitializerWithoutHookup_HasNotRunAtTheEndOfTheSubmission()
        ScriptModeConformance.AssertRuns(
            "Class Sink" & vbCrLf &
            "    Public Shared Touched As Boolean" & vbCrLf &
            "    Public Shared Sub Mark()" & vbCrLf &
            "        Touched = True" & vbCrLf &
            "    End Sub" & vbCrLf &
            "End Class" & vbCrLf &
            "Shared Function F() As Integer" & vbCrLf &
            "    Sink.Mark()" & vbCrLf &
            "    Return 7" & vbCrLf &
            "End Function" & vbCrLf &
            "Shared a As Integer = F()" & vbCrLf &
            "Return Not Sink.Touched",
            True)
    End Sub

    ''' <summary>
    ''' The paired positive of the laziness lock (issue 18-A, the non-vacuous "读了 ⇒ 断到值" half): reading the
    ''' shared field <c>a</c> is what triggers the submission class's shared constructor, so the value the
    ''' initializer produced (<c>7</c>) reads back and the initializer's side effect (<c>Sink.Touched</c>) has by
    ''' then happened. Reading <c>a</c> is the trigger, not the observation - the side effect is checked through a
    ''' separate type (<c>Sink</c>), so this is not the "顶层读该字段" vacuum control the issue forbids.
    ''' </summary>
    <Fact>
    Public Sub SharedFieldInitializer_ReadsBackItsValueAndItsSideEffect_WhenTheFieldIsTouched()
        ScriptModeConformance.AssertRuns(
            "Class Sink" & vbCrLf &
            "    Public Shared Touched As Boolean" & vbCrLf &
            "End Class" & vbCrLf &
            "Shared Function F() As Integer" & vbCrLf &
            "    Sink.Touched = True" & vbCrLf &
            "    Return 7" & vbCrLf &
            "End Function" & vbCrLf &
            "Shared a As Integer = F()" & vbCrLf &
            "Return a * 10 + If(Sink.Touched, 1, 0)",
            71)
    End Sub


    ''' <summary>
    ''' A shared initializer and a shared hookup coexist in the one synthesized shared constructor, and both are
    ''' effective: the initializer contributes 1 and the handler contributes 100, so 101 is the value that says
    ''' "the field was initialized and the handler was hooked up", in that the handler could not have run at all
    ''' without the host the initializer already forced to exist.
    ''' </summary>
    <Fact>
    Public Sub SharedInitializerAndSharedHookup_BothEffective()
        ScriptModeConformance.AssertRuns(
            SinkSource &
            "Shared Function Init() As Integer" & vbCrLf &
            "    Sink.MarkBy(1)" & vbCrLf &
            "    Return 5" & vbCrLf &
            "End Function" & vbCrLf &
            "Shared z As Integer = Init()" & vbCrLf &
            "Shared Event Ev As System.EventHandler" & vbCrLf &
            "Shared Sub H(s As Object, e As System.EventArgs) Handles MyClass.Ev" & vbCrLf &
            "    Sink.MarkBy(100)" & vbCrLf &
            "End Sub" & vbCrLf &
            "Shared Sub Fire()" & vbCrLf &
            "    RaiseEvent Ev(Nothing, System.EventArgs.Empty)" & vbCrLf &
            "End Sub" & vbCrLf &
            "Fire()" & vbCrLf &
            "Return Sink.Count() * 10 + z",
            1015)
    End Sub

#End Region

#Region "T6 the hookup belongs to the type, not to an instance"

    ''' <summary>
    ''' The submission chain creates one instance per submission, and the handler is hooked up once for the type:
    ''' the raise happens in a later submission than the declaration, and a single delivery is counted. Had the
    ''' hookup been placed in the script initializer of the submission class, every instance created by the chain
    ''' would have added a registration and the count would have grown with the chain.
    ''' </summary>
    <Fact>
    Public Sub SharedHookup_SubmissionChain_DoesNotHookTheHandlerTwice()
        Dim declaration = SinkSource &
            "Shared Event Ev As System.EventHandler" & vbCrLf &
            "Shared Sub H(s As Object, e As System.EventArgs) Handles MyClass.Ev" & vbCrLf &
            "    Sink.Mark()" & vbCrLf &
            "End Sub" & vbCrLf &
            "Shared Sub Fire()" & vbCrLf &
            "    RaiseEvent Ev(Nothing, System.EventArgs.Empty)" & vbCrLf &
            "End Sub"

        Dim result = ScriptModeConformance.RunChain(
            New Dictionary(Of String, String)(),
            "main.vbx",
            declaration,
            "Dim first As Integer = 1",
            "Dim second As Integer = 2",
            "Fire()",
            "Return Sink.Count()")

        Assert.Equal(1, result)
    End Sub

    ''' <summary>
    ''' The same script run twice: every run gets a fresh instance of the submission class, so an instance level
    ''' hookup would deliver twice on the second run (1 + 2 = 3). The type level hookup is executed once, so the
    ''' counter only advances by the single raise of that run.
    ''' </summary>
    <Fact>
    Public Sub SharedHookup_EveryRunOfTheSameScript_DeliversOnce()
        Dim script = VisualBasicScript.Create(HookupSource("MyClass.", raises:=1), ScriptModeConformance.DefaultOptions)
        Dim diagnostics = script.Compile()

        Assert.True(Not diagnostics.Any(Function(d) d.Severity = DiagnosticSeverity.Error),
                    String.Join(", ", diagnostics.Select(Function(d) d.Id)))

        Assert.Equal(1, script.RunAsync().GetAwaiter().GetResult().ReturnValue)
        Assert.Equal(2, script.RunAsync().GetAwaiter().GetResult().ReturnValue)
    End Sub

#End Region

#Region "R1 / R2 / R3 the neighbours are unchanged"

    ''' <summary>
    ''' A shared hookup of a class that the script declares, which already had its host: unchanged shape, unchanged
    ''' delivery.
    ''' </summary>
    <Fact>
    Public Sub ClassDeclaredByScript_SharedHandles_StillDelivers()
        ScriptModeConformance.AssertRuns(
            SinkSource &
            "Class Hook" & vbCrLf &
            "    Shared Event Ev As System.EventHandler" & vbCrLf &
            "    Shared Sub Fire()" & vbCrLf &
            "        RaiseEvent Ev(Nothing, System.EventArgs.Empty)" & vbCrLf &
            "    End Sub" & vbCrLf &
            "    Shared Sub H(s As Object, e As System.EventArgs) Handles MyClass.Ev" & vbCrLf &
            "        Sink.Mark()" & vbCrLf &
            "    End Sub" & vbCrLf &
            "End Class" & vbCrLf &
            "Hook.Fire()" & vbCrLf &
            "Hook.Fire()" & vbCrLf &
            "Return Sink.Count()",
            2)
    End Sub

    ''' <summary>
    ''' <c>Shared WithEvents</c>: the hookup is done by the synthesized setter of the variable, not by a shared
    ''' constructor, and it keeps delivering. This task does not touch that host.
    ''' </summary>
    <Fact>
    Public Sub SharedWithEventsHookup_StillDeliversOnce()
        ScriptModeConformance.AssertRuns(
            SinkSource &
            "Class Raiser" & vbCrLf &
            "    Public Event SomethingHappened As System.EventHandler" & vbCrLf &
            "    Public Sub RaiseIt()" & vbCrLf &
            "        RaiseEvent SomethingHappened(Me, System.EventArgs.Empty)" & vbCrLf &
            "    End Sub" & vbCrLf &
            "End Class" & vbCrLf &
            "Shared WithEvents hookedy As New Raiser" & vbCrLf &
            "Shared Sub H(s As Object, e As System.EventArgs) Handles hookedy.SomethingHappened" & vbCrLf &
            "    Sink.Mark()" & vbCrLf &
            "End Sub" & vbCrLf &
            "hookedy.RaiseIt()" & vbCrLf &
            "Return Sink.Count()",
            1)
    End Sub

    ''' <summary>
    ''' The synthesis does not widen what a <c>Handles</c> clause may name: a shared event reached through a type
    ''' name is still not a <c>WithEvents</c> variable, and the clause is still rejected with the diagnostic it has
    ''' always produced. No host is fabricated for it either (the submission class stays without a shared
    ''' constructor), which is what the emit side of this task pins.
    ''' </summary>
    <Fact>
    Public Sub SharedEventReachedThroughATypeName_IsStillRejected()
        ScriptModeConformance.AssertReports(
            SinkSource &
            "Class Hook" & vbCrLf &
            "    Shared Event Ev As System.EventHandler" & vbCrLf &
            "    Shared Sub Fire()" & vbCrLf &
            "        RaiseEvent Ev(Nothing, System.EventArgs.Empty)" & vbCrLf &
            "    End Sub" & vbCrLf &
            "End Class" & vbCrLf &
            "Shared Sub H(s As Object, e As System.EventArgs) Handles Hook.Ev" & vbCrLf &
            "    Sink.Mark()" & vbCrLf &
            "End Sub",
            "BC30506")
    End Sub

#End Region
End Class
