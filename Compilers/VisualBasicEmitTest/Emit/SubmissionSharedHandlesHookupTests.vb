' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System
Imports System.Linq
Imports Microsoft.CodeAnalysis.Test.Utilities
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Roslyn.Test.Utilities

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests.Emit

    ''' <summary>
    ''' A <c>Handles</c> clause whose method and event are both shared is hooked up in the shared constructor of its
    ''' containing class (<c>SourceMemberMethodSymbol.BindSingleHandlesClause</c> takes
    ''' <c>ContainingType.SharedConstructors(0)</c>). The submission class is a class container, so the clause is
    ''' legal in it, but a submission class was only ever given a shared constructor when it had shared
    ''' initializers: without one there was no host, and reaching for <c>SharedConstructors(0)</c> threw an
    ''' <c>IndexOutOfRangeException</c> out of <c>GetDiagnostics</c> and out of <c>Emit</c>.
    ''' <see cref="SourceMemberContainerTypeSymbol.AddWithEventsHookupConstructorsIfNeeded"/> now synthesizes the
    ''' host for exactly that shape, and these tests pin the shape of the host it produces.
    ''' </summary>
    Public Class SubmissionSharedHandlesHookupTests
        Inherits BasicTestBase

        ''' <summary>
        ''' The counter lives in a class the script declares: reading it never initializes the submission class, so
        ''' a delivered handler cannot be reported as a side effect of the observation itself.
        ''' </summary>
        Private Const SinkSource As String =
            "Class Sink" & vbCrLf &
            "    Shared N As Integer" & vbCrLf &
            "    Shared Sub Mark()" & vbCrLf &
            "        N = N + 1" & vbCrLf &
            "    End Sub" & vbCrLf &
            "    Shared Function Count() As Integer" & vbCrLf &
            "        Return N" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Class" & vbCrLf

        ''' <summary>
        ''' A shared event and a shared handler of the submission class, and no shared initializer: the shape that
        ''' used to have no host. The raise goes through a shared method because a top-level <c>RaiseEvent</c>
        ''' statement is not a legal submission statement (<c>BC30188</c>).
        ''' </summary>
        Private Shared Function SharedHookupSource(container As String) As String
            Return SinkSource &
                   "Shared Event Ev As System.EventHandler" & vbCrLf &
                   "Shared Sub H(s As Object, e As System.EventArgs) Handles " & container & "Ev" & vbCrLf &
                   "    Sink.Mark()" & vbCrLf &
                   "End Sub" & vbCrLf &
                   "Shared Sub Fire()" & vbCrLf &
                   "    RaiseEvent Ev(Nothing, System.EventArgs.Empty)" & vbCrLf &
                   "End Sub" & vbCrLf &
                   "Fire()" & vbCrLf &
                   "System.Console.Write(Sink.Count())"
        End Function

        Private Function CreateScriptCompilation(source As String) As VisualBasicCompilation
            Return CreateSubmission(source, options:=TestOptions.DebugDll)
        End Function

        Private Shared Function SharedHandler(compilation As VisualBasicCompilation) As MethodSymbol
            Return compilation.ScriptClass.GetMembers("H").OfType(Of MethodSymbol)().Single()
        End Function

#Region "the hookup host of the submission class"

        ''' <summary>
        ''' The shape that used to end the compiler: <c>Handles MyClass.Ev</c> with no shared initializer. The
        ''' submission class now has exactly one shared constructor, it is the parameterless one (a shared
        ''' constructor cannot take the submission array parameter), and the clause is hooked up in it.
        ''' </summary>
        <Fact>
        Public Sub SharedHandlesInSubmission_SynthesizesParameterlessSharedConstructor()
            Dim compilation = CreateScriptCompilation(SharedHookupSource("MyClass."))

            compilation.VerifyDiagnostics()

            Dim scriptClass = compilation.ScriptClass
            Assert.Equal(1, scriptClass.SharedConstructors.Length)

            Dim host = scriptClass.SharedConstructors(0)
            Assert.Equal(MethodKind.SharedConstructor, host.MethodKind)
            Assert.Equal(0, host.ParameterCount)
            Assert.True(host.IsImplicitlyDeclared)

            Dim handled = SharedHandler(compilation).HandledEvents
            Assert.Equal(1, handled.Length)
            Assert.Equal(MethodKind.SharedConstructor, handled(0).hookupMethod.MethodKind)
            Assert.True(handled(0).hookupMethod.IsShared)
        End Sub

        ''' <summary>
        ''' <c>Handles Me.Ev</c> is the same clause with the other keyword container that names an event of the
        ''' containing class, so it gets the same host.
        ''' </summary>
        <Fact>
        Public Sub SharedHandlesWithMeKeyword_SynthesizesSharedConstructor()
            Dim compilation = CreateScriptCompilation(SharedHookupSource("Me."))

            compilation.VerifyDiagnostics()

            Assert.Equal(1, compilation.ScriptClass.SharedConstructors.Length)
            Assert.Equal(MethodKind.SharedConstructor, SharedHandler(compilation).HandledEvents(0).hookupMethod.MethodKind)
        End Sub

        ''' <summary>
        ''' The host is synthesized once: two shared handlers of the same event share the one shared constructor
        ''' rather than adding a second one, which is what the <c>SharedConstructors(0)</c> of the binder assumes.
        ''' </summary>
        <Fact>
        Public Sub TwoSharedHandlers_SingleSynthesizedSharedConstructor()
            Dim compilation = CreateScriptCompilation(
                SinkSource &
                "Shared Event Ev As System.EventHandler" & vbCrLf &
                "Shared Sub H(s As Object, e As System.EventArgs) Handles MyClass.Ev" & vbCrLf &
                "    Sink.Mark()" & vbCrLf &
                "End Sub" & vbCrLf &
                "Shared Sub SecondHandler(s As Object, e As System.EventArgs) Handles MyClass.Ev" & vbCrLf &
                "    Sink.Mark()" & vbCrLf &
                "    Sink.Mark()" & vbCrLf &
                "End Sub")

            compilation.VerifyDiagnostics()

            Assert.Equal(1, compilation.ScriptClass.SharedConstructors.Length)
            Assert.Equal(
                MethodKind.SharedConstructor,
                compilation.ScriptClass.GetMembers("SecondHandler").OfType(Of MethodSymbol)().Single().HandledEvents(0).hookupMethod.MethodKind)

            ' The one host carries both registrations.
            Dim il = CompileAndVerify(compilation, verify:=Verification.FailsPEVerify).VisualizeIL("Script..cctor")
            Assert.True(il.Contains("""Sub Script.H(Object, System.EventArgs)"""), il)
            Assert.True(il.Contains("""Sub Script.SecondHandler(Object, System.EventArgs)"""), il)
            Assert.Equal(2, il.Split(ControlChars.Lf).Count(Function(line) line.Contains("System.EventHandler..ctor")))
        End Sub

        ''' <summary>
        ''' The shared field initializer and the shared hookup share the one host, and the order inside it is
        ''' observable: the hookup is injected first, the initializer follows, so a handler that runs from the very
        ''' first raise already sees the initialized state.
        ''' </summary>
        <Fact>
        Public Sub SharedInitializerAndSharedHookup_OneHostCarriesBoth()
            Dim compilation = CreateScriptCompilation(
                SinkSource &
                "Shared z As Integer = 5" & vbCrLf &
                "Shared Event Ev As System.EventHandler" & vbCrLf &
                "Shared Sub H(s As Object, e As System.EventArgs) Handles MyClass.Ev" & vbCrLf &
                "    Sink.Mark()" & vbCrLf &
                "End Sub")

            compilation.VerifyDiagnostics()

            Assert.Equal(1, compilation.ScriptClass.SharedConstructors.Length)

            Dim il = CompileAndVerify(compilation, verify:=Verification.FailsPEVerify).VisualizeIL("Script..cctor")
            Assert.True(il.Contains("Script.add_Ev"), il)
            Assert.True(il.Contains("stsfld     ""Script.z As Integer"""), il)
            Assert.True(il.IndexOf("Script.add_Ev") < il.IndexOf("Script.z As Integer"), il)
        End Sub

#End Region

#Region "no host is fabricated where none is needed"

        ''' <summary>
        ''' A submission that only reads the counter gets no shared constructor at all: the synthesis is driven by
        ''' the shared hookup, not applied to every submission class.
        ''' </summary>
        <Fact>
        Public Sub SubmissionWithoutSharedHookup_HasNoSharedConstructor()
            Dim compilation = CreateScriptCompilation(
                SinkSource &
                "System.Console.Write(Sink.Count())")

            compilation.VerifyDiagnostics()

            Assert.Equal(0, compilation.ScriptClass.SharedConstructors.Length)
        End Sub

        ''' <summary>
        ''' A shared handler of an instance event is hosted by an instance constructor, which the submission class
        ''' always has (the synthesized submission constructor). The event being not shared must not add a shared
        ''' constructor - that would move the field initializers of such a script to eager execution.
        ''' </summary>
        <Fact>
        Public Sub SharedHandlerOfInstanceEvent_IsHostedByTheSubmissionConstructorOnly()
            Dim compilation = CreateScriptCompilation(
                SinkSource &
                "Event Ev2 As System.EventHandler" & vbCrLf &
                "Shared Sub H(s As Object, e As System.EventArgs) Handles Me.Ev2" & vbCrLf &
                "    Sink.Mark()" & vbCrLf &
                "End Sub")

            compilation.VerifyDiagnostics()

            Assert.Equal(0, compilation.ScriptClass.SharedConstructors.Length)

            Dim handled = SharedHandler(compilation).HandledEvents
            Assert.Equal(1, handled.Length)
            Assert.Equal(MethodKind.Constructor, handled(0).hookupMethod.MethodKind)
        End Sub

        ''' <summary>
        ''' A <c>WithEvents</c> container is hosted by the synthesized setter of the variable, never by a shared
        ''' constructor. The shared constructor this shape does have comes from the initializer of the shared
        ''' <c>WithEvents</c> variable itself.
        ''' </summary>
        <Fact>
        Public Sub SharedWithEventsHookup_StillHostedByThePropertySetter()
            Dim compilation = CreateScriptCompilation(
                SinkSource &
                "Class Raiser" & vbCrLf &
                "    Event SomethingHappened As System.EventHandler" & vbCrLf &
                "    Sub RaiseIt()" & vbCrLf &
                "        RaiseEvent SomethingHappened(Me, System.EventArgs.Empty)" & vbCrLf &
                "    End Sub" & vbCrLf &
                "End Class" & vbCrLf &
                "Shared WithEvents hookedy As New Raiser" & vbCrLf &
                "Shared Sub H(s As Object, e As System.EventArgs) Handles hookedy.SomethingHappened" & vbCrLf &
                "    Sink.Mark()" & vbCrLf &
                "End Sub")

            compilation.VerifyDiagnostics()

            Dim handled = SharedHandler(compilation).HandledEvents
            Assert.Equal(1, handled.Length)
            Assert.Equal(MethodKind.PropertySet, handled(0).hookupMethod.MethodKind)
            Assert.Equal("set_hookedy", handled(0).hookupMethod.Name)
        End Sub

        ''' <summary>
        ''' The same clause in a class that the script declares - a container that already got its host before this
        ''' fix - keeps its own shared constructor, and the submission class stays without one.
        ''' </summary>
        <Fact>
        Public Sub ClassDeclaredByScript_KeepsItsOwnHostAndNeedsNoneOnTheSubmission()
            Dim compilation = CreateScriptCompilation(
                SinkSource &
                "Class Hook" & vbCrLf &
                "    Shared Event Ev As System.EventHandler" & vbCrLf &
                "    Shared Sub Fire()" & vbCrLf &
                "        RaiseEvent Ev(Nothing, System.EventArgs.Empty)" & vbCrLf &
                "    End Sub" & vbCrLf &
                "    Shared Sub H(s As Object, e As System.EventArgs) Handles MyClass.Ev" & vbCrLf &
                "        Sink.Mark()" & vbCrLf &
                "    End Sub" & vbCrLf &
                "End Class")

            compilation.VerifyDiagnostics()

            Assert.Equal(0, compilation.ScriptClass.SharedConstructors.Length)

            Dim hook = compilation.ScriptClass.GetTypeMembers("Hook").Single()
            Assert.Equal(1, hook.SharedConstructors.Length)
            Assert.Equal(MethodKind.SharedConstructor, hook.GetMembers("H").OfType(Of MethodSymbol)().Single().HandledEvents(0).hookupMethod.MethodKind)
        End Sub

#End Region

#Region "the body of the host"

        ''' <summary>
        ''' The synthesized host of the submission class is the same IL a regular class produces for this clause:
        ''' a delegate over the shared method with a <c>Nothing</c> target, handed to the shared <c>add</c> accessor.
        ''' A shared constructor has no <c>Me</c> to use, and none appears.
        ''' </summary>
        <Fact>
        Public Sub SharedHostBody_IsTheSharedAddHandlerCall_NoMe()
            Dim compilation = CreateScriptCompilation(SharedHookupSource("MyClass."))
            Dim verifier = CompileAndVerify(compilation, verify:=Verification.FailsPEVerify)

            verifier.VerifyIL("Script..cctor",
"{
  // Code size       18 (0x12)
  .maxstack  2
  IL_0000:  ldnull
  IL_0001:  ldftn      ""Sub Script.H(Object, System.EventArgs)""
  IL_0007:  newobj     ""Sub System.EventHandler..ctor(Object, System.IntPtr)""
  IL_000c:  call       ""Sub Script.add_Ev(System.EventHandler)""
  IL_0011:  ret
}")

            Dim il = verifier.VisualizeIL("Script..cctor")
            Assert.DoesNotContain("ldarg", il)
            Assert.DoesNotContain("Me", il)
        End Sub

        ''' <summary>
        ''' The positive control, a regular compilation with the same clause: it delivers, and its host is the
        ''' parameterless shared constructor whose IL matches the synthesized one of the submission class above.
        ''' </summary>
        <Fact>
        Public Sub RegularCompilation_SameClause_DeliversOnce()
            Dim compilation = CreateCompilationWithMscorlib45AndVBRuntime(
                <compilation name="SharedHandlesControl">
                    <file name="a.vb"><![CDATA[
Class Sink
    Shared N As Integer
    Shared Sub Mark()
        N = N + 1
    End Sub
    Shared Function Count() As Integer
        Return N
    End Function
End Class

Class Program
    Shared Event Ev As System.EventHandler

    Shared Sub H(s As Object, e As System.EventArgs) Handles MyClass.Ev
        Sink.Mark()
    End Sub

    Shared Sub Main()
        RaiseEvent Ev(Nothing, System.EventArgs.Empty)
        System.Console.Write(Sink.Count())
    End Sub
End Class
]]></file>
                </compilation>,
                options:=TestOptions.DebugExe)

            Dim verifier = CompileAndVerify(compilation, expectedOutput:="1")

            verifier.VerifyIL("Program..cctor",
"{
  // Code size       18 (0x12)
  .maxstack  2
  IL_0000:  ldnull
  IL_0001:  ldftn      ""Sub Program.H(Object, System.EventArgs)""
  IL_0007:  newobj     ""Sub System.EventHandler..ctor(Object, System.IntPtr)""
  IL_000c:  call       ""Sub Program.add_Ev(System.EventHandler)""
  IL_0011:  ret
}")
        End Sub

#End Region

#Region "cross-submission shared Handles (issue 31)"

    ''' <summary>
    ''' The event is a Shared Event declared in an EARLIER submission; the handler is in the later one. Before
    ''' issue 31 the collector only ensured a shared constructor when it saw the event in the same submission's
    ''' members, so the later submission had none and <c>BindSingleHandlesClause</c> threw
    ''' <c>IndexOutOfRangeException</c> out of <c>SharedConstructors(0)</c>. Now the collector also ensures the
    ''' host when the event is not in this submission's own members (the binder resolves it along the chain), so
    ''' the hookup lands in a synthesized shared constructor. Red before the fix (ICE), green after.
    ''' </summary>
    <Fact>
    Public Sub CrossSubmissionSharedHandles_SynthesizesHostAndWiresUp()
        Dim first = CreateScriptCompilation(
            SinkSource &
            "Shared Event Ev As System.EventHandler")
        first.VerifyDiagnostics()

        Dim second = CreateSubmission(
            "Shared Sub H(s As Object, e As System.EventArgs) Handles Me.Ev" & vbCrLf &
            "    Sink.Mark()" & vbCrLf &
            "End Sub",
            options:=TestOptions.DebugDll, previous:=first)

        second.VerifyDiagnostics()

        Assert.Equal(1, second.ScriptClass.SharedConstructors.Length)
        Assert.Equal(
            MethodKind.SharedConstructor,
            SharedHandler(second).HandledEvents(0).hookupMethod.MethodKind)
    End Sub

    ''' <summary>
    ''' Counter-lock for the broadened host synthesis: a shared handler whose event is declared in NO submission
    ''' must still be reported by the binder (ERR_EventNotFound1) and must not throw - the extra shared
    ''' constructor is inert because the submission never emits. Guards against the collector change silently
    ''' swallowing the missing-event diagnostic.
    ''' </summary>
    <Fact>
    Public Sub CrossSubmissionMissingEvent_StillReportsNotFound()
        Dim first = CreateScriptCompilation(
            SinkSource &
            "Shared Event Other As System.EventHandler")
        first.VerifyDiagnostics()

        Dim second = CreateSubmission(
            "Shared Sub H(s As Object, e As System.EventArgs) Handles Me.NoSuchEvent" & vbCrLf &
            "    Sink.Mark()" & vbCrLf &
            "End Sub",
            options:=TestOptions.DebugDll, previous:=first)

        ' Must not throw (before the fix this threw IndexOutOfRangeException), and the missing event is
        ' still reported as an error (the broadened host synthesis must not mask it).
        Dim diags = second.GetDiagnostics().ToArray()
        Assert.True(diags.Any(Function(d) d.Severity = DiagnosticSeverity.Error),
                    "the missing event must still be reported; got: " & String.Join(", ", diags.Select(Function(d) d.Id)))
    End Sub

#End Region

    End Class
End Namespace
