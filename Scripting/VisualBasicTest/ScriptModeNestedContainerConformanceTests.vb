' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System
Imports Microsoft.CodeAnalysis.Scripting
Imports Xunit

''' <summary>
''' The nested container cells of the syntax ledger (test-plan §C.3.A-§C.3.F): every construct whose script side is
''' written <em>inside a type the script itself declares</em> rather than directly at the top level of the unit.
''' <para>
''' The two containers are not interchangeable for these cells. Each declaration here becomes a nested type of the
''' submission class, so the initializer, constructor, event and extension member paths all run on a type whose
''' enclosing type is the script class - which is the shape a plain <c>Compilers\VisualBasicTest</c> case cannot
''' produce. The ledger rows this file closes are the ones the coverage audit could not exempt for exactly that
''' reason (<c>test-plan.md</c> §C.3.M, "已知受容器影响" family list).
''' </para>
''' <para>
''' Everything is in memory and runs inside this process: no file, process, registry or network access.
''' </para>
''' </summary>
Public Class ScriptModeNestedContainerConformanceTests

    ''' <summary>
    ''' <c>System.Linq.Enumerable</c> lives in an assembly the default script reference set does not carry, and the
    ''' collection contract of <c>expressions.md:898</c> lists the assembly level imports as a source of extension
    ''' methods. The reference is added rather than assumed, so the cell pins the collection rule and not the default
    ''' reference set.
    ''' </summary>
    Private Shared ReadOnly s_linqOptions As ScriptOptions = ScriptModeConformance.DefaultOptions.
        AddReferences(GetType(Enumerable).Assembly)

    ' ---- §C.3.B 事件 · Handles 子句 ----

    ''' <summary>
    ''' A <c>Handles</c> clause written on a method of a type the script declares, over a <c>WithEvents</c> variable of
    ''' that same type. The hookup is done by the synthesized constructor of <c>Listener</c>, and the count proves the
    ''' handler really ran: a clause that was parsed but not wired up leaves it at 0.
    ''' </summary>
    <Fact>
    Public Sub NestedHandlesClause_Delivers()
        ScriptModeConformance.AssertRuns(
            "Class Hook" & vbCrLf &
            "    Public Event SomethingHappened As System.EventHandler" & vbCrLf &
            "    Public Sub RaiseIt()" & vbCrLf &
            "        RaiseEvent SomethingHappened(Me, System.EventArgs.Empty)" & vbCrLf &
            "    End Sub" & vbCrLf &
            "End Class" & vbCrLf &
            "Class Listener" & vbCrLf &
            "    Public WithEvents Hooked As New Hook" & vbCrLf &
            "    Public Count As Integer" & vbCrLf &
            "    Public Sub OnIt(s As Object, e As System.EventArgs) Handles Hooked.SomethingHappened" & vbCrLf &
            "        Count += 1" & vbCrLf &
            "    End Sub" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim made As New Listener" & vbCrLf &
            "made.Hooked.RaiseIt()" & vbCrLf &
            "Return made.Count", 1)
    End Sub

    ' ---- §C.3.C 扩展方法 · 声明与收集 ----

    ''' <summary>
    ''' An <c>&lt;Extension&gt;</c> method declared by a <c>Module</c> the script itself declares. The declaration is
    ''' accepted in that container (BC36551 "extension methods can only be defined in modules" would fire on a nested
    ''' <c>Class</c> - the counterpart case below), and the body is reachable and runs.
    ''' </summary>
    <Fact>
    Public Sub NestedExtensionMethodInDeclaredModule_IsCallableThroughTheModule()
        ScriptModeConformance.AssertRuns(
            "Imports System.Runtime.CompilerServices" & vbCrLf &
            "Module StringExtensions" & vbCrLf &
            "    <Extension>" & vbCrLf &
            "    Public Function Twice(s As String) As String" & vbCrLf &
            "        Return s & s" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Module" & vbCrLf &
            "Return StringExtensions.Twice(""abc"")", "abcabc")
    End Sub

    ''' <summary>
    ''' The container rule of extension methods, pinned where it visibly changes the outcome: a module the script
    ''' declares is nested inside the submission class, and a type nested in a type is not a standard module as far as
    ''' extension collection goes (<c>Symbols\Source\SourceMemberContainerTypeSymbol.vb:3366</c> requires the
    ''' containing symbol to be a namespace; <c>Symbols\NamedTypeSymbolExtensions.vb:110</c> shows the script class
    ''' itself is the container the language recognizes). The same declaration written at the script top level resolves
    ''' from this call site - see the two cases below. So the receiver invocation is BC30456 here, not a run time
    ''' failure.
    ''' </summary>
    <Fact>
    Public Sub NestedExtensionMethodInDeclaredModule_IsNotCollected_IsReported()
        ScriptModeConformance.AssertReports(
            "Imports System.Runtime.CompilerServices" & vbCrLf &
            "Module StringExtensions" & vbCrLf &
            "    <Extension>" & vbCrLf &
            "    Public Function Twice(s As String) As String" & vbCrLf &
            "        Return s & s" & vbCrLf &
            "    End Function" & vbCrLf &
            "    Public Function Use() As String" & vbCrLf &
            "        Return ""abc"".Twice()" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Module" & vbCrLf &
            "Return StringExtensions.Use()", "BC30456")
    End Sub

    ''' <summary>
    ''' An <c>&lt;Extension&gt;</c> method declared by a <c>Class</c> the script declares: BC36551
    ''' (<c>ERR_ExtensionMethodNotInModule</c>), the declaration rule of <c>type-members.md:906</c>.
    ''' </summary>
    <Fact>
    Public Sub NestedExtensionMethodInDeclaredClass_IsReported()
        ScriptModeConformance.AssertReports(
            "Imports System.Runtime.CompilerServices" & vbCrLf &
            "Class StringExtensions" & vbCrLf &
            "    <Extension>" & vbCrLf &
            "    Public Shared Function Twice(s As String) As String" & vbCrLf &
            "        Return s & s" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Class", "BC36551")
    End Sub

    ''' <summary>
    ''' The collection cell: the extension method is a member of the submission class (the top level declaration form
    ''' <c>ScriptModeConformanceTests.vb</c> <c>TopLevelSharedExtensionMethod_Conforms</c> already pins), and the
    ''' member access expression that has to find it sits in a method body of a type the script declares.
    ''' </summary>
    <Fact>
    Public Sub ExtensionMethodCallInsideNestedTypeMethod_IsCollected()
        ScriptModeConformance.AssertRuns(
            "Imports System.Runtime.CompilerServices" & vbCrLf &
            "<Extension>" & vbCrLf &
            "Shared Function Twice(s As String) As String" & vbCrLf &
            "    Return s & s" & vbCrLf &
            "End Function" & vbCrLf &
            "Class Consumer" & vbCrLf &
            "    Public Function Use() As String" & vbCrLf &
            "        Return ""abc"".Twice()" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim made As New Consumer" & vbCrLf &
            "Return made.Use()", "abcabc")
    End Sub

    ''' <summary>
    ''' The other arm of extension collection: the imports of the source file (<c>expressions.md:898</c> step 3) reach
    ''' a library extension method from a method body of a type the script declares. <c>Where</c> and <c>Count</c> are
    ''' both extensions of <c>System.Linq.Enumerable</c>, so the expression exercises the collection twice.
    ''' </summary>
    <Fact>
    Public Sub LinqExtensionCallInsideNestedTypeMethod_IsCollected()
        ScriptModeConformance.AssertRuns(
            "Imports System.Linq" & vbCrLf &
            "Class Consumer" & vbCrLf &
            "    Public Function Use() As Integer" & vbCrLf &
            "        Return {1, 2, 3}.Where(Function(x) x > 1).Count()" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim made As New Consumer" & vbCrLf &
            "Return made.Use()", 2, s_linqOptions)
    End Sub

    ' ---- §C.3.B 构造器 · 字段初始化器 · 常量 ----

    ''' <summary>
    ''' A <c>Shared Sub New</c> declared by a type the script declares. The counter is read through the type's own
    ''' shared member, so 42 then 43 shows the constructor ran exactly once, before the first use, and did not run
    ''' again per call.
    ''' </summary>
    <Fact>
    Public Sub NestedSharedConstructor_RunsOnceBeforeFirstUse()
        ScriptModeConformance.AssertRuns(
            "Class Holder" & vbCrLf &
            "    Public Shared Counter As Integer" & vbCrLf &
            "    Shared Sub New()" & vbCrLf &
            "        Counter = 41" & vbCrLf &
            "    End Sub" & vbCrLf &
            "    Public Shared Function Bump() As Integer" & vbCrLf &
            "        Counter += 1" & vbCrLf &
            "        Return Counter" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Class" & vbCrLf &
            "Return Holder.Bump() & ""/"" & Holder.Bump()", "42/43")
    End Sub

    ''' <summary>Instance and shared field declarations, both read back off an instance of the declaring type.</summary>
    <Fact>
    Public Sub NestedInstanceAndSharedFields_Conform()
        ScriptModeConformance.AssertRuns(
            "Class Holder" & vbCrLf &
            "    Public Shared sx As Integer = 5" & vbCrLf &
            "    Public ix As Integer = 7" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim made As New Holder" & vbCrLf &
            "Return made.ix + Holder.sx", 12)
    End Sub

    ''' <summary>Read only field declarations of both flavors on a type the script declares.</summary>
    <Fact>
    Public Sub NestedReadOnlyFields_Conform()
        ScriptModeConformance.AssertRuns(
            "Class Holder" & vbCrLf &
            "    Public Shared ReadOnly sx As Integer = 5" & vbCrLf &
            "    Public ReadOnly ix As Integer = 7" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim made As New Holder" & vbCrLf &
            "Return made.ix + Holder.sx", 12)
    End Sub

    ''' <summary>
    ''' The three initializer forms of a nested type read together: a shared <c>=</c> initializer, an instance
    ''' <c>=</c> initializer, and a shared <c>ReadOnly</c> one. The total is the sum of the three, so an initializer
    ''' that never ran shows up as a smaller number, not as an exception.
    ''' </summary>
    <Fact>
    Public Sub NestedFieldInitializers_AllRun()
        ScriptModeConformance.AssertRuns(
            "Class Holder" & vbCrLf &
            "    Public Shared sx As Integer = 5" & vbCrLf &
            "    Public ix As Integer = 7" & vbCrLf &
            "    Public Shared ReadOnly ro As Integer = 11" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim made As New Holder" & vbCrLf &
            "Return made.ix + Holder.sx + Holder.ro", 23)
    End Sub

    ''' <summary>The regular initializer form (<c>=</c>), instance and shared, on a nested type.</summary>
    <Fact>
    Public Sub NestedRegularInitializers_Conform()
        ScriptModeConformance.AssertRuns(
            "Class Holder" & vbCrLf &
            "    Public iy As Integer = 7" & vbCrLf &
            "    Public Shared sharedValue As Integer = 5" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim made As New Holder" & vbCrLf &
            "Return made.iy + Holder.sharedValue", 12)
    End Sub

    ''' <summary>
    ''' The array upper bound initializer form, on a shared and an instance field of a nested type. VB arrays are zero
    ''' based, so 3 and 6 are the declared bounds plus one - a field that kept the default <c>Nothing</c> would throw
    ''' instead.
    ''' </summary>
    <Fact>
    Public Sub NestedArraySizeInitializers_Conform()
        ScriptModeConformance.AssertRuns(
            "Class Holder" & vbCrLf &
            "    Public Shared arr(2) As Integer" & vbCrLf &
            "    Public grid(1, 2) As Integer" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim made As New Holder" & vbCrLf &
            "Return Holder.arr.Length + made.grid.Length", 9)
    End Sub

    ''' <summary>Auto implemented properties with and without an initializer, on a type the script declares.</summary>
    <Fact>
    Public Sub NestedAutoImplementedProperties_Conform()
        ScriptModeConformance.AssertRuns(
            "Class Holder" & vbCrLf &
            "    Public Property Auto As Integer" & vbCrLf &
            "    Public Shared Property SharedAuto As String = ""init""" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim made As New Holder" & vbCrLf &
            "made.Auto = 4" & vbCrLf &
            "Return made.Auto & Holder.SharedAuto", "4init")
    End Sub

    ''' <summary>Member constants of a nested <c>Class</c> and of a nested <c>Module</c>, read from top level code.</summary>
    <Fact>
    Public Sub NestedMemberConstants_Conform()
        ScriptModeConformance.AssertRuns(
            "Class Holder" & vbCrLf &
            "    Public Const k As Integer = 3" & vbCrLf &
            "    Public Const s As String = ""abc""" & vbCrLf &
            "End Class" & vbCrLf &
            "Module Helpers" & vbCrLf &
            "    Public Const j As Integer = 4" & vbCrLf &
            "End Module" & vbCrLf &
            "Return Holder.k + Holder.s.Length + Helpers.j", 10)
    End Sub

    ' ---- §C.3.B 自定义事件 ----

    ''' <summary>
    ''' A <c>Custom Event</c> with all three accessors, declared by a type the script declares. Every accessor body
    ''' runs: registration goes through <c>AddHandler</c>, the first raise reaches the handler, <c>RemoveHandler</c>
    ''' takes it away and the second raise does not - so the count is 1 and not 0 or 2.
    ''' </summary>
    <Fact>
    Public Sub NestedCustomEventAccessorsAndRegistration_Conform()
        ScriptModeConformance.AssertRuns(
            "Class Raiser" & vbCrLf &
            "    Private _handlers As System.EventHandler" & vbCrLf &
            "    Public Custom Event Changed As System.EventHandler" & vbCrLf &
            "        AddHandler(value As System.EventHandler)" & vbCrLf &
            "            _handlers = CType(System.Delegate.Combine(_handlers, value), System.EventHandler)" & vbCrLf &
            "        End AddHandler" & vbCrLf &
            "        RemoveHandler(value As System.EventHandler)" & vbCrLf &
            "            _handlers = CType(System.Delegate.Remove(_handlers, value), System.EventHandler)" & vbCrLf &
            "        End RemoveHandler" & vbCrLf &
            "        RaiseEvent(sender As Object, e As System.EventArgs)" & vbCrLf &
            "            If _handlers IsNot Nothing Then _handlers(sender, e)" & vbCrLf &
            "        End RaiseEvent" & vbCrLf &
            "    End Event" & vbCrLf &
            "    Public Sub Hook(handler As System.EventHandler)" & vbCrLf &
            "        AddHandler Changed, handler" & vbCrLf &
            "    End Sub" & vbCrLf &
            "    Public Sub Unhook(handler As System.EventHandler)" & vbCrLf &
            "        RemoveHandler Changed, handler" & vbCrLf &
            "    End Sub" & vbCrLf &
            "    Public Sub Fire()" & vbCrLf &
            "        RaiseEvent Changed(Me, System.EventArgs.Empty)" & vbCrLf &
            "    End Sub" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim made As New Raiser" & vbCrLf &
            "Dim count As Integer = 0" & vbCrLf &
            "Dim handler As System.EventHandler = Sub(s As Object, e As System.EventArgs) count += 1" & vbCrLf &
            "made.Hook(handler)" & vbCrLf &
            "made.Fire()" & vbCrLf &
            "made.Unhook(handler)" & vbCrLf &
            "made.Fire()" & vbCrLf &
            "Return count", 1)
    End Sub

    ' ---- §C.3.C 重载决议 · 运行时函数 ----

    ''' <summary>Overloads declared by a nested type, resolved from a call site in another method of that type.</summary>
    <Fact>
    Public Sub NestedOverloadResolution_Conforms()
        ScriptModeConformance.AssertRuns(
            "Class Speaker" & vbCrLf &
            "    Public Overloads Function Tell(x As Integer) As String" & vbCrLf &
            "        Return ""I"" & x" & vbCrLf &
            "    End Function" & vbCrLf &
            "    Public Overloads Function Tell(x As String) As String" & vbCrLf &
            "        Return ""S"" & x" & vbCrLf &
            "    End Function" & vbCrLf &
            "    Public Function Use() As String" & vbCrLf &
            "        Return Tell(1) & Tell(""a"")" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim made As New Speaker" & vbCrLf &
            "Return made.Use()", "I1Sa")
    End Sub

    ''' <summary>The conditional runtime functions called from a method body of a type the script declares.</summary>
    <Fact>
    Public Sub NestedConditionalRuntimeFunctions_Conform()
        ScriptModeConformance.AssertRuns(
            "Imports Microsoft.VisualBasic" & vbCrLf &
            "Class Speaker" & vbCrLf &
            "    Public Function Use() As String" & vbCrLf &
            "        Return IIf(True, ""yes"", ""no"") & ""/"" & Choose(2, 10, 20, 30) & ""/"" & Switch(False, ""a"", True, ""b"")" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim made As New Speaker" & vbCrLf &
            "Return made.Use()", "yes/20/b")
    End Sub

    ' ---- §C.3.C MyBase ----

    ''' <summary>
    ''' <c>MyBase</c> inside an overriding method of a type the script declares. The base half of the string is the
    ''' only thing the non virtual call can produce, so a receiver that had resolved to <c>Me</c> would recurse and
    ''' the expected value could not be reached. (An explicit <c>MyBase</c> at the script top level is BC36966 - that
    ''' is the restriction the top level cell pins, not this one.)
    ''' </summary>
    <Fact>
    Public Sub NestedMyBaseExpression_Conforms()
        ScriptModeConformance.AssertRuns(
            "Class ShapeBase" & vbCrLf &
            "    Public Overridable Function Describe() As String" & vbCrLf &
            "        Return ""base""" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Class" & vbCrLf &
            "Class Square" & vbCrLf &
            "    Inherits ShapeBase" & vbCrLf &
            "    Public Overrides Function Describe() As String" & vbCrLf &
            "        Return MyBase.Describe() & ""/square""" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim made As New Square" & vbCrLf &
            "Return made.Describe()", "base/square")
    End Sub

    ' ---- §C.3.F Static 局部的三种容器 ----

    ''' <summary>
    ''' The lambda container: a multiline lambda in a method body of a type the script declares. A <c>Static</c> local
    ''' is not allowed in one - BC36672 (<c>ERR_StaticInLambda</c>).
    ''' </summary>
    <Fact>
    Public Sub NestedStaticLocalInLambda_IsReported()
        ScriptModeConformance.AssertReports(
            "Class Holder" & vbCrLf &
            "    Public Sub Go()" & vbCrLf &
            "        Dim action As System.Action = Sub()" & vbCrLf &
            "                                       Static n As Integer = 0" & vbCrLf &
            "                                       n += 1" & vbCrLf &
            "                                   End Sub" & vbCrLf &
            "        action()" & vbCrLf &
            "    End Sub" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim made As New Holder" & vbCrLf &
            "made.Go()", "BC36672")
    End Sub

    ''' <summary>
    ''' The <c>Structure</c> container: a <c>Structure</c> declared by a type the script declares. BC31400
    ''' (<c>ERR_BadStaticLocalInStruct</c>); the method is reached through an instance so the cell is not merely about
    ''' the declaration.
    ''' </summary>
    <Fact>
    Public Sub NestedStaticLocalInStructureMethod_IsReported()
        ScriptModeConformance.AssertReports(
            "Class Outer" & vbCrLf &
            "    Public Structure Holder" & vbCrLf &
            "        Public Sub Touch()" & vbCrLf &
            "            Static n As Integer = 0" & vbCrLf &
            "            n += 1" & vbCrLf &
            "        End Sub" & vbCrLf &
            "    End Structure" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim made As New Outer.Holder" & vbCrLf &
            "made.Touch()", "BC31400")
    End Sub

    ''' <summary>
    ''' The generic method container: BC32068 (<c>ERR_BadStaticLocalInGenericMethod</c>). The method is declared by a
    ''' type the script declares, so the rule runs on a script member and not on a nested type.
    ''' </summary>
    <Fact>
    Public Sub NestedStaticLocalInGenericMethod_IsReported()
        ScriptModeConformance.AssertReports(
            "Class Holder" & vbCrLf &
            "    Public Sub Touch(Of T)()" & vbCrLf &
            "        Static n As Integer = 0" & vbCrLf &
            "        n += 1" & vbCrLf &
            "    End Sub" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim made As New Holder" & vbCrLf &
            "made.Touch(Of Integer)()", "BC32068")
    End Sub

    ''' <summary>
    ''' The async container, with an initializer: BC36955 (<c>ERR_BadStaticInitializerInResumable</c>), on an
    ''' <c>Async Function</c> of a type the script declares.
    ''' </summary>
    <Fact>
    Public Sub NestedStaticLocalInAsyncMethod_IsReported()
        ScriptModeConformance.AssertReports(
            "Imports System.Threading.Tasks" & vbCrLf &
            "Class Holder" & vbCrLf &
            "    Public Async Function Go() As Task(Of Integer)" & vbCrLf &
            "        Static n As Integer = 0" & vbCrLf &
            "        Await Task.Delay(1)" & vbCrLf &
            "        n += 1" & vbCrLf &
            "        Return n" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim made As New Holder" & vbCrLf &
            "Return Await made.Go()", "BC36955")
    End Sub

    ' ---- §C.3.E `?` Print 语句 ----

    ''' <summary>
    ''' The <c>? x</c> print statement in a method body is BC31003 (<c>ERR_UnexpectedExpressionStatement</c>):
    ''' <c>ParsePrintStatement</c> (<c>Parser\ParseStatement.vb:1896-1910</c>) accepts the form only when the token
    ''' after the statement is the end of the file, and a method body always has a terminator left. The case pins that
    ''' the construct has no legal nested form; the statement's own container is the top level of a submission.
    ''' </summary>
    <Fact>
    Public Sub NestedPrintStatement_IsReported()
        ScriptModeConformance.AssertReports(
            "Sub Go()" & vbCrLf &
            "    ? 1 + 2" & vbCrLf &
            "End Sub" & vbCrLf &
            "Go()", "BC31003")
    End Sub

End Class
