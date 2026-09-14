' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System
Imports System.Collections.Generic
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
