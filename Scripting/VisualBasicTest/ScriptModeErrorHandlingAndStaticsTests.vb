' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System
Imports System.Collections.Generic
Imports Microsoft.CodeAnalysis.Scripting
Imports Xunit

''' <summary>
''' Directive, <c>Static</c> local and unstructured error handling cells of the syntax ledger (test-plan §C.3.F).
''' These are the constructs whose top level container is a script and nothing else: a <c>#</c> directive sits in the
''' top level token stream, a <c>Static</c> local can only live in a method body, and <c>On Error</c> / <c>Err</c>
''' are rejected by the async script initializer but accepted by a method the submission class declares itself.
''' <para>
''' The two containers are therefore used deliberately and are named per cell. A construct the script top level
''' carries directly is written there (<c>#</c> directives, <c>Shadows</c> on a submission member). A construct that
''' needs a method body is written in a <c>Sub</c> / <c>Function</c> whose declaration is itself a top level statement
''' of the script - the submission class member, which is the script specific container an ordinary compilation only
''' reaches with a project file. Nothing here is wrapped in a helper class of the test: the declaration the cell pins
''' is the script's own.
''' </para>
''' <para>
''' Every diagnostic id was read from a probe of the built <c>vbi.exe</c> and matched against <c>Errors.vb</c>:
''' <c>ERR_ExpectedConditionalDirective</c> (BC30248, <c>Errors.vb:265</c>, reported by
''' <c>ParseConditional.vb:565</c>), <c>ERR_StaticInLambda</c> (BC36672), <c>ERR_BadStaticLocalInStruct</c> (BC31400),
''' <c>ERR_BadStaticLocalInGenericMethod</c> (BC32068), <c>ERR_BadStaticInitializerInResumable</c> (BC36955) and
''' <c>ERR_MultilineLambdasCannotContainOnError</c> (BC36668), all of them reported by
''' <c>Binding\Binder_Statements.vb</c> for the statement forms.
''' </para>
''' <para>
''' Everything is in memory and runs inside this process: no file, process, registry or network access. The
''' <c>Err</c> cells run in a method body, which is where the unstructured handling they pin is legal, and each one
''' leaves the object cleared so no state of this process depends on the order the cells run in.
''' </para>
''' </summary>
Public Class ScriptModeErrorHandlingAndStaticsTests

#Region "T061 bad directives"

    ''' <summary>
    ''' A bare <c>#</c> at the top level of a script: the token starts a directive and the word after it names no
    ''' directive the compiler knows, so <c>ParseBadDirective</c> reports BC30248
    ''' (<c>ERR_ExpectedConditionalDirective</c>, <c>Errors.vb:265</c>). Nothing else in the file is wrong - the field
    ''' and the return after it parse normally - so the id pins the directive token itself.
    ''' </summary>
    <Fact>
    Public Sub TopLevelBareHash_IsReported()
        ScriptModeConformance.AssertReports(
            "Dim x As Integer = 1" & vbCrLf &
            "#" & vbCrLf &
            "Return x", "BC30248")
    End Sub

    ''' <summary>
    ''' The unrecognized name form of the same token. The two forms reach the same <c>ParseBadDirective</c> arm, and
    ''' the neighbouring directive mistakes report something else entirely (an unterminated <c>#If</c> is BC30012, an
    ''' unterminated <c>#Region</c> is BC30681, <c>#ExternalSource</c> outside its pair is BC30579), so BC30248 is
    ''' what "this directive name does not exist" means.
    ''' </summary>
    <Fact>
    Public Sub TopLevelUnrecognizedDirective_IsReported()
        ScriptModeConformance.AssertReports(
            "Dim x As Integer = 1" & vbCrLf &
            "#foo" & vbCrLf &
            "Return x", "BC30248")
    End Sub

#End Region

#Region "T062 - T066 static locals"

    ''' <summary>
    ''' A <c>Static</c> local with no initializer in a <c>Function</c> the script declares at its top level. The
    ''' three values are the whole point of the modifier: a static starts at the default of its type and keeps the
    ''' value the previous call left, so the same call site answers 1, 2, 3 - a plain local would answer 1, 1, 1.
    ''' </summary>
    <Fact>
    Public Sub StaticLocalWithoutInitializer_InTopLevelFunction_Conforms()
        ScriptModeConformance.AssertRuns(
            "Function Bump() As Integer" & vbCrLf &
            "    Static count As Integer" & vbCrLf &
            "    count += 1" & vbCrLf &
            "    Return count" & vbCrLf &
            "End Function" & vbCrLf &
            "Return Bump() & ""/"" & Bump() & ""/"" & Bump()", "1/2/3")
    End Sub

    ''' <summary>
    ''' The initializer form. The initializer runs on the first entry and never again, so the second call continues
    ''' from the value the first left: 41 then 42, not 41 then 41.
    ''' </summary>
    <Fact>
    Public Sub StaticLocalWithInitializer_InTopLevelFunction_Conforms()
        ScriptModeConformance.AssertRuns(
            "Function NextOne() As Integer" & vbCrLf &
            "    Static seed As Integer = 40" & vbCrLf &
            "    seed += 1" & vbCrLf &
            "    Return seed" & vbCrLf &
            "End Function" & vbCrLf &
            "Return NextOne() & ""/"" & NextOne()", "41/42")
    End Sub

    ''' <summary>
    ''' The container cell: a <c>Static</c> local inside a top level <c>Sub</c>, which is a member of the submission
    ''' class. Two such <c>Sub</c>s write one field of that class through implicit <c>Me</c> while each keeps its own
    ''' static, so the total is 100 + 1 + 2 + 10: a single shared static would reach 100 + 1 + 11 + 21 and statics
    ''' that did not persist would reach 100 + 1 + 1 + 10.
    ''' </summary>
    <Fact>
    Public Sub StaticLocalInTopLevelSub_KeepsPerSubStorage_Conforms()
        ScriptModeConformance.AssertRuns(
            "Dim total As Integer = 100" & vbCrLf &
            "Sub First()" & vbCrLf &
            "    Static n As Integer = 0" & vbCrLf &
            "    n += 1" & vbCrLf &
            "    total += n" & vbCrLf &
            "End Sub" & vbCrLf &
            "Sub Second()" & vbCrLf &
            "    Static n As Integer = 0" & vbCrLf &
            "    n += 10" & vbCrLf &
            "    total += n" & vbCrLf &
            "End Sub" & vbCrLf &
            "First()" & vbCrLf &
            "First()" & vbCrLf &
            "Second()" & vbCrLf &
            "Return total", 113)
    End Sub

    ''' <summary>
    ''' The lambda container: a multiline lambda at the top level of the script is a method body, and a
    ''' <c>Static</c> local is not allowed in one - BC36672 (<c>ERR_StaticInLambda</c>), reported by
    ''' <c>Binder_Statements.vb</c> for the local declaration inside the lambda.
    ''' </summary>
    <Fact>
    Public Sub StaticLocalInTopLevelLambda_IsReported()
        ScriptModeConformance.AssertReports(
            "Dim action As System.Action = Sub()" & vbCrLf &
            "    Static n As Integer = 0" & vbCrLf &
            "    n += 1" & vbCrLf &
            "End Sub" & vbCrLf &
            "action()", "BC36672")
    End Sub

    ''' <summary>
    ''' The <c>Structure</c> container: BC31400 (<c>ERR_BadStaticLocalInStruct</c>). The method is reached through
    ''' an instance so the cell is not merely about the declaration.
    ''' </summary>
    <Fact>
    Public Sub StaticLocalInStructureMethod_IsReported()
        ScriptModeConformance.AssertReports(
            "Structure Holder" & vbCrLf &
            "    Public Sub Touch()" & vbCrLf &
            "        Static n As Integer = 0" & vbCrLf &
            "        n += 1" & vbCrLf &
            "    End Sub" & vbCrLf &
            "End Structure" & vbCrLf &
            "Dim held As New Holder" & vbCrLf &
            "held.Touch()", "BC31400")
    End Sub

    ''' <summary>
    ''' The generic method container: BC32068 (<c>ERR_BadStaticLocalInGenericMethod</c>). The method is declared by
    ''' the submission class itself, so the rule runs on a script member and not on a nested type.
    ''' </summary>
    <Fact>
    Public Sub StaticLocalInGenericMethod_IsReported()
        ScriptModeConformance.AssertReports(
            "Sub Touch(Of T)()" & vbCrLf &
            "    Static n As Integer = 0" & vbCrLf &
            "    n += 1" & vbCrLf &
            "End Sub" & vbCrLf &
            "Touch(Of Integer)()", "BC32068")
    End Sub

    ''' <summary>
    ''' The async container, with an initializer: BC36955 (<c>ERR_BadStaticInitializerInResumable</c>). The host is
    ''' the top level <c>Async Function</c> of the submission class, which is the reason the cell belongs to this
    ''' file rather than to a compiler test.
    ''' </summary>
    <Fact>
    Public Sub StaticLocalInAsyncMethod_IsReported()
        ScriptModeConformance.AssertReports(
            "Imports System.Threading.Tasks" & vbCrLf &
            "Async Function Go() As Task(Of Integer)" & vbCrLf &
            "    Static n As Integer = 0" & vbCrLf &
            "    Await Task.Delay(1)" & vbCrLf &
            "    n += 1" & vbCrLf &
            "    Return n" & vbCrLf &
            "End Function" & vbCrLf &
            "Return Await Go()", "BC36955")
    End Sub

#End Region

#Region "T067 - T069 Err and On Error"

    ''' <summary>
    ''' The <c>Err</c> object in a method the submission class declares, in its three states. Default: the number is
    ''' 0 and the description is empty. <c>Err.Raise</c>: the number is the raised one and the description is the
    ''' runtime's message for it. <c>Err.Clear</c>: back to 0. The five fields are read one state at a time, so a
    ''' <c>Raise</c> that never reached the object fails the first half and a <c>Clear</c> that did nothing fails the
    ''' second - the two statements are not interchangeable, which is what the cell has to show.
    ''' </summary>
    <Fact>
    Public Sub ErrObjectDefaultRaiseAndClear_Conform()
        ScriptModeConformance.AssertRuns(
            "Imports Microsoft.VisualBasic" & vbCrLf &
            "Function Probe() As String" & vbCrLf &
            "    Dim initial As Integer = Err.Number" & vbCrLf &
            "    Dim blank As Boolean = Err.Description = """"" & vbCrLf &
            "    On Error Resume Next" & vbCrLf &
            "    Err.Raise(5)" & vbCrLf &
            "    Dim raised As Integer = Err.Number" & vbCrLf &
            "    Dim described As Boolean = Err.Description.Length > 0" & vbCrLf &
            "    Err.Clear()" & vbCrLf &
            "    Dim cleared As Integer = Err.Number" & vbCrLf &
            "    On Error GoTo 0" & vbCrLf &
            "    Return initial & ""/"" & blank & ""/"" & raised & ""/"" & described & ""/"" & cleared" & vbCrLf &
            "End Function" & vbCrLf &
            "Return Probe()", "0/True/5/True/0")
    End Sub

    ''' <summary>
    ''' <c>On Error GoTo 0</c> inside a top level <c>Sub</c>. Two effects are pinned at once and they are the two
    ''' halves of the statement: the error object is cleared (13 becomes 0, the runtime index is reset before the
    ''' handler is chosen, <c>LocalRewriter_UnstructuredExceptionHandling.vb:334-339</c>) and the <c>Resume Next</c>
    ''' handler is disarmed, so the next failure leaves the method and is caught by the top level <c>Try</c> -
    ''' <c>propagated</c>, not <c>survived</c>.
    ''' </summary>
    <Fact>
    Public Sub OnErrorGoToZeroInTopLevelSub_DisarmsTheHandler()
        ScriptModeConformance.AssertRuns(
            "Imports Microsoft.VisualBasic" & vbCrLf &
            "Dim captured As Integer = 0" & vbCrLf &
            "Dim observed As Integer = 0" & vbCrLf &
            "Sub Cause()" & vbCrLf &
            "    Dim bad As Integer = CInt(""boom"")" & vbCrLf &
            "End Sub" & vbCrLf &
            "Sub Probe()" & vbCrLf &
            "    On Error Resume Next" & vbCrLf &
            "    Dim first As Integer = CInt(""first"")" & vbCrLf &
            "    captured = Err.Number" & vbCrLf &
            "    On Error GoTo 0" & vbCrLf &
            "    observed = Err.Number" & vbCrLf &
            "    Cause()" & vbCrLf &
            "End Sub" & vbCrLf &
            "Dim outcome As String = ""none""" & vbCrLf &
            "Try" & vbCrLf &
            "    Probe()" & vbCrLf &
            "    outcome = ""survived""" & vbCrLf &
            "Catch ex As System.Exception" & vbCrLf &
            "    outcome = ""propagated""" & vbCrLf &
            "End Try" & vbCrLf &
            "Return captured & ""/"" & observed & ""/"" & outcome", "13/0/propagated")
    End Sub

    ''' <summary>
    ''' <c>On Error GoTo -1</c> in the same place. It clears the error object exactly like <c>GoTo 0</c> (13 becomes
    ''' 0), but it does not touch the active handler index - the rewrite of that arm assigns the resume target and
    ''' jumps over the handler assignment (<c>LocalRewriter_UnstructuredExceptionHandling.vb:344-350</c>), so the
    ''' <c>Resume Next</c> handler stays armed and the next failure is swallowed: <c>survived</c> where the other
    ''' arm of this pair answers <c>propagated</c>. One token apart, different outcome.
    ''' </summary>
    <Fact>
    Public Sub OnErrorGoToMinusOneInTopLevelSub_KeepsTheHandlerArmed()
        ScriptModeConformance.AssertRuns(
            "Imports Microsoft.VisualBasic" & vbCrLf &
            "Dim captured As Integer = 0" & vbCrLf &
            "Dim observed As Integer = 0" & vbCrLf &
            "Sub Cause()" & vbCrLf &
            "    Dim bad As Integer = CInt(""boom"")" & vbCrLf &
            "End Sub" & vbCrLf &
            "Sub Probe()" & vbCrLf &
            "    On Error Resume Next" & vbCrLf &
            "    Dim first As Integer = CInt(""first"")" & vbCrLf &
            "    captured = Err.Number" & vbCrLf &
            "    On Error GoTo -1" & vbCrLf &
            "    observed = Err.Number" & vbCrLf &
            "    Cause()" & vbCrLf &
            "End Sub" & vbCrLf &
            "Dim outcome As String = ""none""" & vbCrLf &
            "Try" & vbCrLf &
            "    Probe()" & vbCrLf &
            "    outcome = ""survived""" & vbCrLf &
            "Catch ex As System.Exception" & vbCrLf &
            "    outcome = ""propagated""" & vbCrLf &
            "End Try" & vbCrLf &
            "Return captured & ""/"" & observed & ""/"" & outcome", "13/0/survived")
    End Sub

    ''' <summary>
    ''' <c>On Error</c> inside a top level lambda: BC36668
    ''' (<c>ERR_MultilineLambdasCannotContainOnError</c>, the <c>IsInLambda</c> arm of
    ''' <c>BindOnErrorStatement</c>). The lambda is written at the top level of the script, which is the container the
    ''' ledger names.
    ''' </summary>
    <Fact>
    Public Sub OnErrorInsideTopLevelLambda_IsReported()
        ScriptModeConformance.AssertReports(
            "Dim action As System.Action = Sub()" & vbCrLf &
            "    On Error Resume Next" & vbCrLf &
            "End Sub" & vbCrLf &
            "action()", "BC36668")
    End Sub

    ''' <summary>
    ''' The discriminating companion of the cell above: the same top level lambda shape, but the <c>On Error</c> is
    ''' in the method the lambda calls. It compiles and runs, so BC36668 is about the lambda body and not about
    ''' lambdas, or about a lambda calling a method that has a handler.
    ''' </summary>
    <Fact>
    Public Sub LambdaCallingAMethodWithOnError_Conforms()
        ScriptModeConformance.AssertRuns(
            "Sub Swallow()" & vbCrLf &
            "    On Error Resume Next" & vbCrLf &
            "    Dim bad As Integer = CInt(""boom"")" & vbCrLf &
            "    System.Console.WriteLine(bad)" & vbCrLf &
            "End Sub" & vbCrLf &
            "Dim action As System.Action = Sub() Swallow()" & vbCrLf &
            "action()" & vbCrLf &
            "Return ""LAMBDA-OK""", "LAMBDA-OK")
    End Sub

#End Region

#Region "T076 Shadows"

    ''' <summary>
    ''' <c>Shadows</c> on declarations the submission class makes itself, which is the form the script top level
    ''' carries directly. Nothing is shadowed here - <c>Object</c> declares no <c>shadowed</c> member and this is the
    ''' first submission of the chain - so what the cell pins is that the modifier arm of a top level declaration
    ''' (<c>Parser.vb:1029</c>) reaches the member builder and produces members the top level can read and call.
    ''' </summary>
    <Fact>
    Public Sub TopLevelShadowsDeclaration_HidesNothing_Conforms()
        ScriptModeConformance.AssertRuns(
            "Shadows Dim shadowed As Integer = 7" & vbCrLf &
            "Shadows Function Describe() As String" & vbCrLf &
            "    Return ""top""" & vbCrLf &
            "End Function" & vbCrLf &
            "Return shadowed & ""/"" & Describe()", "7/top")
    End Sub

    ''' <summary>
    ''' The hiding rule the modifier is for, on the same shape with the neighbouring modifier swapped in. Both pairs
    ''' declare <c>Tell(x As String)</c> in a class derived from one that declares <c>Tell(x As Integer)</c>, and
    ''' both call <c>Tell(1)</c>; only the modifier differs. <c>Shadows</c> hides the inherited overload, so the call
    ''' binds the <c>String</c> declaration through the implicit conversion of the default <c>Option Strict Off</c>;
    ''' <c>Overloads</c> keeps both in one set and the exact <c>Integer</c> match wins. The second value is
    ''' unreachable with the modifier of the first, which is what makes the cell a discriminator rather than a
    ''' restatement of "the keyword compiles".
    ''' <para>
    ''' The declaration that carries the modifier sits in a class body here rather than at the top level of the
    ''' script because the two shells differ there: measured through the submission chain, a <c>Shadows</c> member of
    ''' a later submission does not narrow an unqualified call - the same <c>Tell(1)</c> answers <c>inherited</c> with
    ''' <c>Shadows</c>, with <c>Overloads</c> and with no modifier at all, so the chain shape cannot tell the three
    ''' apart. The reason is the lookup the submission container uses: <c>Binder_Lookup.vb</c>
    ''' <c>LookupInSubmissions</c> merges each submission's hit into one overload set and never reads either modifier
    ''' (<c>:889-890</c>, comment "always overload (ignore Overloads modifier)"), which is registered as
    ''' <c>InternalDevDocs\issues\issue-shadowing-across-submissions-reports-overload-error.md</c> and pinned by
    ''' <c>ScriptModeApiSurfaceConformanceTests.vb</c>. This cell keeps the discriminating shape and leaves the
    ''' container claim to the method above.
    ''' </para>
    ''' </summary>
    <Fact>
    Public Sub ShadowsAndOverloadsOnTheSameDeclaration_Differ()
        ScriptModeConformance.AssertRuns(
            "Class ShadowBase" & vbCrLf &
            "    Public Function Tell(x As Integer) As String" & vbCrLf &
            "        Return ""inherited""" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Class" & vbCrLf &
            "Class ShadowDerived" & vbCrLf &
            "    Inherits ShadowBase" & vbCrLf &
            "    Public Shadows Function Tell(x As String) As String" & vbCrLf &
            "        Return ""shadowed""" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Class" & vbCrLf &
            "Class OverloadBase" & vbCrLf &
            "    Public Function Tell(x As Integer) As String" & vbCrLf &
            "        Return ""inherited""" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Class" & vbCrLf &
            "Class OverloadDerived" & vbCrLf &
            "    Inherits OverloadBase" & vbCrLf &
            "    Public Overloads Function Tell(x As String) As String" & vbCrLf &
            "        Return ""shadowed""" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim shadowing As New ShadowDerived" & vbCrLf &
            "Dim overloading As New OverloadDerived" & vbCrLf &
            "Return shadowing.Tell(1) & ""/"" & overloading.Tell(1)", "shadowed/inherited")
    End Sub

#End Region

End Class
