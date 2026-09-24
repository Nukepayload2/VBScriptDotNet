' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System
Imports System.Threading.Tasks
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Scripting
Imports Xunit

''' <summary>
''' Statement, reference and option cells of the matrix (design-detailed.md §U7 维度三/四/五). The top level host
''' is the synthesized script initializer, so statements that need a real method or an async context behave
''' differently here than in an ordinary method body - each cell pins which of the two it is.
''' <para>
''' The member declaration cells at the end of the file are here rather than in the declaration class because the
''' declaration they pin is the <c>Sub</c> / <c>Property</c> header a statement list hangs off, not a field of
''' the submission class.
''' </para>
''' </summary>
Public Class ScriptModeStatementConformanceTests

#Region "Await inside blocks"

    ''' <summary>
    ''' Await is legal in every block whose host is the async script initializer: Using, For, While, Do,
    ''' Select Case and With.
    ''' </summary>
    <Fact>
    Public Sub TopLevelAwaitInBlocks_Conform()
        ScriptModeConformance.AssertRuns(
            "Dim seen As Integer = 0" & vbCrLf &
            "Using stream As New System.IO.MemoryStream" & vbCrLf &
            "    Await Task.Delay(1)" & vbCrLf &
            "    seen += 1" & vbCrLf &
            "End Using" & vbCrLf &
            "For i = 1 To 1" & vbCrLf &
            "    Await Task.Delay(1)" & vbCrLf &
            "    seen += 1" & vbCrLf &
            "Next" & vbCrLf &
            "While seen < 2" & vbCrLf &
            "    Await Task.Delay(1)" & vbCrLf &
            "    seen += 1" & vbCrLf &
            "End While" & vbCrLf &
            "Do" & vbCrLf &
            "    Await Task.Delay(1)" & vbCrLf &
            "    seen += 1" & vbCrLf &
            "    Exit Do" & vbCrLf &
            "Loop" & vbCrLf &
            "Select Case seen" & vbCrLf &
            "    Case 3" & vbCrLf &
            "        Await Task.Delay(1)" & vbCrLf &
            "        seen += 1" & vbCrLf &
            "End Select" & vbCrLf &
            "Dim gate As New Object" & vbCrLf &
            "With gate" & vbCrLf &
            "    Await Task.Delay(1)" & vbCrLf &
            "    seen += 1" & vbCrLf &
            "End With" & vbCrLf &
            "Return seen", 5)
    End Sub

    ''' <summary>
    ''' Await directly inside a Finally block is rejected with BC36943. Its host method check runs for method bodies
    ''' only; the top level counterpart is the stub body of the script initializer.
    ''' </summary>
    <Fact>
    Public Sub TopLevelAwaitInFinally_IsReported()
        ScriptModeConformance.AssertReports(
            "Try" & vbCrLf &
            "    System.Console.WriteLine(""T"")" & vbCrLf &
            "Finally" & vbCrLf &
            "    Await Task.Delay(1)" & vbCrLf &
            "End Try", "BC36943")
    End Sub

#End Region

#Region "Yield and Exit"

    ''' <summary>A Yield inside an Iterator method of the submission class is fine; the same statement at the top
    ''' level is not, because its host initializer is not an iterator.</summary>
    <Fact>
    Public Sub TopLevelYieldWithoutIterator_IsReported()
        ScriptModeConformance.AssertReports("Yield 1", "BC30800")
    End Sub

    ''' <summary>Exit For / While / Do / Select inside their own statement, at the top level.</summary>
    <Fact>
    Public Sub TopLevelExitLoopStatements_Conform()
        ScriptModeConformance.AssertRuns(
            "Dim log As String = """"" & vbCrLf &
            "For i = 1 To 5" & vbCrLf &
            "    If i = 2 Then Exit For" & vbCrLf &
            "    log &= i" & vbCrLf &
            "Next" & vbCrLf &
            "For Each value In {1, 2, 3}" & vbCrLf &
            "    Exit For" & vbCrLf &
            "Next" & vbCrLf &
            "Dim j As Integer = 0" & vbCrLf &
            "While True" & vbCrLf &
            "    j += 1" & vbCrLf &
            "    If j = 3 Then Exit While" & vbCrLf &
            "End While" & vbCrLf &
            "Do" & vbCrLf &
            "    Exit Do" & vbCrLf &
            "Loop" & vbCrLf &
            "Select Case 1" & vbCrLf &
            "    Case 1" & vbCrLf &
            "        Exit Select" & vbCrLf &
            "End Select" & vbCrLf &
            "log &= j" & vbCrLf &
            "Return log", "13")
    End Sub

    ''' <summary>Exit Sub / Exit Function inside a method of the submission class.</summary>
    <Fact>
    Public Sub TopLevelExitSubAndFunctionInMethod_Conform()
        ScriptModeConformance.AssertRuns(
            "Function Value1() As Integer" & vbCrLf &
            "    Exit Function" & vbCrLf &
            "End Function" & vbCrLf &
            "Sub Do1()" & vbCrLf &
            "    Exit Sub" & vbCrLf &
            "End Sub" & vbCrLf &
            "Do1()" & vbCrLf &
            "Return Value1()", 0)
    End Sub

    ''' <summary>A bare Exit Sub at the top level: its host is a function, so BC30065 is reported.</summary>
    <Fact>
    Public Sub TopLevelExitSubStatement_IsReported()
        ScriptModeConformance.AssertReports("Exit Sub", "BC30065")
    End Sub

    ''' <summary>Continue For / While / Do, at the top level.</summary>
    <Fact>
    Public Sub TopLevelContinueStatements_Conform()
        ScriptModeConformance.AssertRuns(
            "Dim log As String = """"" & vbCrLf &
            "For i = 1 To 4" & vbCrLf &
            "    If i Mod 2 = 0 Then Continue For" & vbCrLf &
            "    log &= i" & vbCrLf &
            "Next" & vbCrLf &
            "Dim j As Integer = 0" & vbCrLf &
            "While j < 4" & vbCrLf &
            "    j += 1" & vbCrLf &
            "    If j = 2 Then Continue While" & vbCrLf &
            "    log &= j" & vbCrLf &
            "End While" & vbCrLf &
            "Do" & vbCrLf &
            "    Continue Do" & vbCrLf &
            "Loop Until True" & vbCrLf &
            "Return log", "13134")
    End Sub

#End Region

#Region "GoTo, labels, On Error"

    ''' <summary>GoTo out of a For, a Using and a While block at the top level.</summary>
    <Fact>
    Public Sub TopLevelGoToOutOfBlocks_Conform()
        ScriptModeConformance.AssertRuns(
            "Dim total As Integer = 0" & vbCrLf &
            "Dim hit As String = """"" & vbCrLf &
            "For i = 1 To 5" & vbCrLf &
            "    total += i" & vbCrLf &
            "    If i = 3 Then GoTo afterFor" & vbCrLf &
            "Next" & vbCrLf &
            "afterFor:" & vbCrLf &
            "Using stream As New System.IO.MemoryStream" & vbCrLf &
            "    hit = ""using""" & vbCrLf &
            "    GoTo afterUsing" & vbCrLf &
            "End Using" & vbCrLf &
            "afterUsing:" & vbCrLf &
            "Dim k As Integer = 0" & vbCrLf &
            "While True" & vbCrLf &
            "    k += 1" & vbCrLf &
            "    If k = 2 Then GoTo afterWhile" & vbCrLf &
            "End While" & vbCrLf &
            "afterWhile:" & vbCrLf &
            "Return total & ""/"" & hit & ""/"" & k", "6/using/2")
    End Sub

    ''' <summary>A jump into a For block is rejected by the binder - the block entry is not a jump target.</summary>
    <Fact>
    Public Sub TopLevelGoToIntoLoop_IsReported()
        ScriptModeConformance.AssertReports(
            "GoTo inside" & vbCrLf &
            "For i = 1 To 3" & vbCrLf &
            "inside:" & vbCrLf &
            "    System.Console.WriteLine(i)" & vbCrLf &
            "Next", "BC30757")
    End Sub

    ''' <summary>
    ''' On Error / Resume inside a method of the submission class is ordinary VB. The top level counterpart is
    ''' rejected (BC36956) because the statements cannot appear in the async script initializer.
    ''' </summary>
    <Fact>
    Public Sub OnErrorInsideMethod_Conforms()
        ScriptModeConformance.AssertRuns(
            "Sub Swallow()" & vbCrLf &
            "    On Error Resume Next" & vbCrLf &
            "    Dim bad As Integer = CInt(""not a number"")" & vbCrLf &
            "    System.Console.WriteLine(bad)" & vbCrLf &
            "End Sub" & vbCrLf &
            "Sub Jump()" & vbCrLf &
            "    On Error GoTo Fault" & vbCrLf &
            "    Dim bad As Integer = CInt(""bad"")" & vbCrLf &
            "    System.Console.WriteLine(""not reached"")" & vbCrLf &
            "    Exit Sub" & vbCrLf &
            "Fault:" & vbCrLf &
            "    System.Console.WriteLine(""FAULT"")" & vbCrLf &
            "End Sub" & vbCrLf &
            "Swallow()" & vbCrLf &
            "Jump()" & vbCrLf &
            "Return ""ERRORS-HANDLED""", "ERRORS-HANDLED")
    End Sub

    ''' <summary>The top level counterpart: BC36956, the diagnostic for On Error in an async method or iterator.</summary>
    <Fact>
    Public Sub TopLevelOnErrorStatement_IsReported()
        ScriptModeConformance.AssertReports(
            "On Error Resume Next" & vbCrLf &
            "Dim value As Integer = CInt(""not a number"")" & vbCrLf &
            "Return value", "BC36956")
    End Sub

#End Region

#Region "Blocks and loops"

    ''' <summary>Using, SyncLock (on a field and on a nested instance) at the top level.</summary>
    <Fact>
    Public Sub TopLevelUsingAndSyncLock_Conform()
        ScriptModeConformance.AssertRuns(
            "Dim text As New System.Text.StringBuilder" & vbCrLf &
            "Using writer As New System.IO.StringWriter(text)" & vbCrLf &
            "    writer.Write(""U"")" & vbCrLf &
            "End Using" & vbCrLf &
            "Dim gate As New Object" & vbCrLf &
            "Dim locked As Boolean = False" & vbCrLf &
            "SyncLock gate" & vbCrLf &
            "    locked = True" & vbCrLf &
            "End SyncLock" & vbCrLf &
            "SyncLock text" & vbCrLf &
            "    locked = locked AndAlso True" & vbCrLf &
            "End SyncLock" & vbCrLf &
            "Return text.ToString() & ""/"" & locked", "U/True")
    End Sub

    ''' <summary>With over a reference type and over a nested Structure.</summary>
    <Fact>
    Public Sub TopLevelWithBlock_Conform()
        ScriptModeConformance.AssertRuns(
            "Dim text As New System.Text.StringBuilder" & vbCrLf &
            "With text" & vbCrLf &
            "    .Append(""W"")" & vbCrLf &
            "    .Append(1)" & vbCrLf &
            "End With" & vbCrLf &
            "Structure Pair" & vbCrLf &
            "    Public Left As Integer" & vbCrLf &
            "    Public Right As Integer" & vbCrLf &
            "End Structure" & vbCrLf &
            "Dim coordinates As New Pair With {.Left = 1, .Right = 2}" & vbCrLf &
            "With coordinates" & vbCrLf &
            "    .Left += 10" & vbCrLf &
            "End With" & vbCrLf &
            "Return text.ToString() & ""/"" & coordinates.Left & ""/"" & coordinates.Right", "W1/11/2")
    End Sub

    ''' <summary>Select Case over an Integer (including a range and a relational arm) and over a String.</summary>
    <Fact>
    Public Sub TopLevelSelectCase_Conform()
        ScriptModeConformance.AssertRuns(
            "Dim log As String = """"" & vbCrLf &
            "For n = 0 To 11 Step 11" & vbCrLf &
            "    Select Case n" & vbCrLf &
            "        Case 0" & vbCrLf &
            "            log &= ""zero""" & vbCrLf &
            "        Case 1, 2" & vbCrLf &
            "            log &= ""small""" & vbCrLf &
            "        Case Is > 10" & vbCrLf &
            "            log &= ""big""" & vbCrLf &
            "    End Select" & vbCrLf &
            "Next" & vbCrLf &
            "Dim text As String = ""b""" & vbCrLf &
            "Select Case text" & vbCrLf &
            "    Case ""a""" & vbCrLf &
            "        log &= ""/a""" & vbCrLf &
            "    Case ""b""" & vbCrLf &
            "        log &= ""/text""" & vbCrLf &
            "    Case Else" & vbCrLf &
            "        log &= ""/other""" & vbCrLf &
            "End Select" & vbCrLf &
            "Return log", "zerobig/text")
    End Sub

    ''' <summary>For / For Each / While / Do (three forms), every loop running at the top level.</summary>
    <Fact>
    Public Sub TopLevelLoops_Conform()
        ScriptModeConformance.AssertRuns(
            "Dim total As Integer = 0" & vbCrLf &
            "For i = 1 To 3" & vbCrLf &
            "    total += i" & vbCrLf &
            "Next" & vbCrLf &
            "For Each text In {""a"", ""bb""}" & vbCrLf &
            "    total += text.Length" & vbCrLf &
            "Next" & vbCrLf &
            "Dim k As Integer = 0" & vbCrLf &
            "While k < 2" & vbCrLf &
            "    k += 1" & vbCrLf &
            "End While" & vbCrLf &
            "Do" & vbCrLf &
            "    k += 1" & vbCrLf &
            "Loop Until k >= 4" & vbCrLf &
            "Do While k < 5" & vbCrLf &
            "    k += 1" & vbCrLf &
            "Loop" & vbCrLf &
            "total += k" & vbCrLf &
            "Return total", 14)
    End Sub

    ''' <summary>
    ''' ReDim / ReDim Preserve / Erase on top level array fields. Erase lowers to an assignment of Nothing for
    ''' every array shape (<c>Binder_Statements.vb:818-849</c>), so both the ReDim'd and the fixed-size field read
    ''' back as Nothing - the shape that used to reach code generation with an unlowered node.
    ''' </summary>
    <Fact>
    Public Sub TopLevelReDimAndErase_Conform()
        ScriptModeConformance.AssertRuns(
            "Dim fixedArr(2) As Integer" & vbCrLf &
            "fixedArr(0) = 7" & vbCrLf &
            "Dim dynamicArr() As Integer = {1, 2, 3}" & vbCrLf &
            "ReDim Preserve dynamicArr(4)" & vbCrLf &
            "dynamicArr(4) = 9" & vbCrLf &
            "ReDim dynamicArr(1)" & vbCrLf &
            "Dim shrunk As Integer = dynamicArr.Length" & vbCrLf &
            "Erase fixedArr" & vbCrLf &
            "Erase dynamicArr" & vbCrLf &
            "Return shrunk & ""/"" & CStr(fixedArr Is Nothing) & ""/"" & CStr(dynamicArr Is Nothing)", "2/True/True")
    End Sub

    ''' <summary>
    ''' AddHandler / RemoveHandler / RaiseEvent against an event of a nested class. The delivery is asserted, not
    ''' just the compile: the handler count shows the add and the remove both took effect.
    ''' </summary>
    <Fact>
    Public Sub NestedClassEventAddRemoveHandler_Conforms()
        ScriptModeConformance.AssertRuns(
            "Class Button" & vbCrLf &
            "    Public Event Clicked As System.EventHandler" & vbCrLf &
            "    Public Sub Click()" & vbCrLf &
            "        RaiseEvent Clicked(Me, System.EventArgs.Empty)" & vbCrLf &
            "    End Sub" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim clicker As New Button" & vbCrLf &
            "Dim count As Integer = 0" & vbCrLf &
            "Dim handler As System.EventHandler = Sub(s As Object, e As System.EventArgs) count += 1" & vbCrLf &
            "AddHandler clicker.Clicked, handler" & vbCrLf &
            "clicker.Click()" & vbCrLf &
            "RemoveHandler clicker.Clicked, handler" & vbCrLf &
            "clicker.Click()" & vbCrLf &
            "Return count", 1)
    End Sub

    ''' <summary>
    ''' A RaiseEvent statement cannot appear directly at the top level of a script: the parser reports
    ''' BC30188/BC30205 instead of running it (the same raise inside a top level Sub or lambda is fine, which the
    ''' event cells of the two other files cover).
    ''' </summary>
    <Fact>
    Public Sub TopLevelRaiseEventStatement_IsReported()
        ScriptModeConformance.AssertReports(
            "Event Changed As System.EventHandler" & vbCrLf &
            "AddHandler Changed, Sub(s As Object, e As System.EventArgs) System.Console.WriteLine(""x"")" & vbCrLf &
            "RaiseEvent Changed(Nothing, System.EventArgs.Empty)" & vbCrLf &
            "Return ""OK""", "BC30188")
    End Sub

#End Region

#Region "End / Stop and the release emission path"

    ''' <summary>
    ''' End cannot be produced by a script at all, so the cell never reaches a running artifact: the top level
    ''' statement is BC30678 (<c>ERR_UnrecognizedEnd</c>), the same statement inside a method is BC30615
    ''' (<c>ERR_EndDisallowedInDllProjects</c>).
    ''' </summary>
    <Fact>
    Public Sub TopLevelEndStatement_IsReported()
        ScriptModeConformance.AssertReports(
            "System.Console.WriteLine(""BEFORE-END"")" & vbCrLf &
            "End", "BC30678")
    End Sub

    <Fact>
    Public Sub EndInsideMethod_IsReported()
        ScriptModeConformance.AssertReports(
            "Sub Finish()" & vbCrLf &
            "    System.Console.WriteLine(""BEFORE-END"")" & vbCrLf &
            "    End" & vbCrLf &
            "End Sub" & vbCrLf &
            "Finish()", "BC30615")
    End Sub

    ''' <summary>
    ''' Stop compiles and emits, but running it would hand the process to a debugger (a no-op only while none is
    ''' attached), so the observable half of the cell is the artifact. The probe host measured it as a no-op:
    ''' exit code 0 with the output before it flushed.
    ''' </summary>
    <Fact>
    Public Sub TopLevelStopStatement_EmitsWithoutRunning()
        ScriptModeConformance.AssertEmits(
            "System.Console.WriteLine(""BEFORE-STOP"")" & vbCrLf &
            "Stop")
    End Sub

    ''' <summary>
    ''' The optimized emission path: the probes were all debug builds, and the iterator/async capture walker branches
    ''' on the optimization level. Same shapes as the debug cells, with /optimize+ semantics.
    ''' </summary>
    <Fact>
    Public Sub ReleaseOptimizedIteratorAndAsync_Conform()
        ScriptModeConformance.AssertRuns(
            "Iterator Function CountTo(n As Integer) As System.Collections.Generic.IEnumerable(Of Integer)" & vbCrLf &
            "    For i = 1 To n" & vbCrLf &
            "        Yield i" & vbCrLf &
            "    Next" & vbCrLf &
            "End Function" & vbCrLf &
            "Dim total As Integer = 0" & vbCrLf &
            "For Each value In CountTo(4)" & vbCrLf &
            "    total += value" & vbCrLf &
            "Next" & vbCrLf &
            "Dim doubled As System.Func(Of System.Threading.Tasks.Task(Of Integer)) = Async Function()" & vbCrLf &
            "                                                                                         Await Task.Delay(1)" & vbCrLf &
            "                                                                                         Return total * 2" & vbCrLf &
            "                                                                                     End Function" & vbCrLf &
            "Return Await doubled()", 20,
            ScriptModeConformance.DefaultOptions.WithOptimizationLevel(OptimizationLevel.Release))
    End Sub

    ''' <summary>The release variant of the field/shared initializer shapes.</summary>
    <Fact>
    Public Sub ReleaseOptimizedFieldInitializers_Conform()
        ScriptModeConformance.AssertRuns(
            "Shared sharedValue As Integer = 5" & vbCrLf &
            "Shared ReadOnly sharedReadOnly As Integer = 7" & vbCrLf &
            "Dim instanceValue As Integer = 9" & vbCrLf &
            "Return sharedValue + sharedReadOnly + instanceValue", 21,
            ScriptModeConformance.DefaultOptions.WithOptimizationLevel(OptimizationLevel.Release))
    End Sub

#End Region

#Region "References"

    ''' <summary>Implicit Me: a top level Sub reads and writes a field without naming the receiver.</summary>
    <Fact>
    Public Sub TopLevelImplicitMe_Conforms()
        ScriptModeConformance.AssertRuns(
            "Dim count As Integer = 0" & vbCrLf &
            "Sub Bump()" & vbCrLf &
            "    count += 1" & vbCrLf &
            "End Sub" & vbCrLf &
            "Bump()" & vbCrLf &
            "Bump()" & vbCrLf &
            "Return count", 2)
    End Sub

    ''' <summary>
    ''' An explicit 'Me' in top-level script code is BC36966: the ban spans the whole script class, so a member the
    ''' script class declares is no escape either - which is what the member-body cells in this file and the next region
    ''' pin. (scope restored to the C# scripting shape by script-class-explicit-keyword-parity-revert / decisions.md D7)
    ''' </summary>
    <Fact>
    Public Sub TopLevelExplicitMe_IsReported()
        ScriptModeConformance.AssertReports("Return Me.ToString()", "BC36966")
    End Sub

    ''' <summary>
    ''' An explicit 'Me' in the body of a Function the submission class declares is BC36966: the ban spans the whole
    ''' script class, so the member body is not ordinary class code any more (C# scripting parity, D7 revert).
    ''' </summary>
    <Fact>
    Public Sub TopLevelExplicitMeInMethodBody_IsReported()
        ScriptModeConformance.AssertReports(
            "Dim count As Integer = 5" & vbCrLf &
            "Function ReadField() As Integer" & vbCrLf &
            "    Return Me.count" & vbCrLf &
            "End Function" & vbCrLf &
            "Return ReadField()", "BC36966")
    End Sub

    ''' <summary>
    ''' The same scope for an explicit 'MyClass': BC36966 in the body of a member of the submission class too.
    ''' </summary>
    <Fact>
    Public Sub TopLevelExplicitMyClassInMethodBody_IsReported()
        ScriptModeConformance.AssertReports(
            "Dim count As Integer = 5" & vbCrLf &
            "Function ReadViaMyClass() As Integer" & vbCrLf &
            "    Return MyClass.count" & vbCrLf &
            "End Function" & vbCrLf &
            "Return ReadViaMyClass()", "BC36966")
    End Sub

    ''' <summary>
    ''' The 'My' namespace exists in script mode but has no Application/Computer members (BC30456), so the shape is a
    ''' diagnostic instead of a naming crash.
    ''' </summary>
    <Fact>
    Public Sub TopLevelMyNamespace_IsReported()
        ScriptModeConformance.AssertReports(
            "Return My.Application.Info.AssemblyName", "BC30456")
    End Sub

    ''' <summary>The same for My.Computer: the name resolves, the member does not.</summary>
    <Fact>
    Public Sub TopLevelMyNamespaceComputer_IsReported()
        ScriptModeConformance.AssertReports(
            "Dim computer = My.Computer" & vbCrLf &
            "Return ""MY""", "BC30456")
    End Sub

#End Region

#Region "Explicit Me / MyClass / MyBase scope (script-class-explicit-me-scope)"

    ''' <summary>
    ''' A2: the initializer of a top level variable belongs to the field it declares, but it is still top-level script
    ''' code, so the explicit 'Me' is BC36966 there. This is the position a ready-made "am I binding the global
    ''' statements" predicate answers False for, which is why it is pinned next to A4.
    ''' </summary>
    <Fact>
    Public Sub TopLevelExplicitMeInFieldInitializer_IsReported()
        ScriptModeConformance.AssertReports(
            "Dim count As Integer = 5" & vbCrLf &
            "Dim copied As Integer = Me.count" & vbCrLf &
            "Return copied", "BC36966")
    End Sub

    ''' <summary>
    ''' A4: a lambda written in the global statements is top-level script code too, and the other alarm line: the shape
    ''' sits inside a block body just like B4 below, so only the member the lambda is written in tells the two apart.
    ''' </summary>
    <Fact>
    Public Sub TopLevelExplicitMeInLambda_IsReported()
        ScriptModeConformance.AssertReports(
            "Dim count As Integer = 5" & vbCrLf &
            "Dim getter As System.Func(Of Integer) = Function() Me.count" & vbCrLf &
            "Return getter()", "BC36966")
    End Sub

    ''' <summary>
    ''' B2: the shadowing escape hatch is closed again. Inside a member body the qualified 'Me.count' that once reached
    ''' the field while a same-spelling local shadowed it is refused BC36966, exactly as the C# scripting dialect rejects
    ''' an explicit 'this' there (D7 parity revert). The source shape is kept unchanged to pin that the hatch is gone.
    ''' </summary>
    <Fact>
    Public Sub TopLevelExplicitMeAgainstShadowingLocal_IsReported()
        ScriptModeConformance.AssertReports(
            "Dim count As Integer = 5" & vbCrLf &
            "Function ReadField() As Integer" & vbCrLf &
            "    Dim count As Integer = 7" & vbCrLf &
            "    Return Me.count" & vbCrLf &
            "End Function" & vbCrLf &
            "Function ReadLocal() As Integer" & vbCrLf &
            "    Dim count As Integer = 9" & vbCrLf &
            "    Return count" & vbCrLf &
            "End Function" & vbCrLf &
            "Return ReadField() * 10 + ReadLocal()", "BC36966")
    End Sub

    ''' <summary>
    ''' B4: the pair of A4. A lambda written in the body of a member of the submission class takes the same ban as its
    ''' enclosing member, so the explicit 'Me' is BC36966 (whole-script-class scope, C# parity).
    ''' </summary>
    <Fact>
    Public Sub TopLevelExplicitMeInLambdaInsideMethodBody_IsReported()
        ScriptModeConformance.AssertReports(
            "Dim count As Integer = 5" & vbCrLf &
            "Function ReadViaLambda() As Integer" & vbCrLf &
            "    Dim getter As System.Func(Of Integer) = Function() Me.count + 1" & vbCrLf &
            "    Return getter()" & vbCrLf &
            "End Function" & vbCrLf &
            "Return ReadViaLambda()", "BC36966")
    End Sub

    ''' <summary>
    ''' C1: a Shared member of the submission class has no instance, and that ordinary answer (BC30043, "'Me' is valid
    ''' only within an instance method") is what the shape gets now - the script rule does not preempt it.
    ''' </summary>
    <Fact>
    Public Sub SharedMethodExplicitMe_IsReported()
        ScriptModeConformance.AssertReports(
            "Dim count As Integer = 5" & vbCrLf &
            "Shared Function ReadShared() As Integer" & vbCrLf &
            "    Return Me.count" & vbCrLf &
            "End Function" & vbCrLf &
            "Return ReadShared()", "BC30043")
    End Sub

    ''' <summary>
    ''' D1: an explicit 'MyBase' in the body of an instance member is refused BC36966 as well. The whole-script-class ban
    ''' is restored and the old 'MyBase'-to-<c>System.Object</c> fallback is gone, so the shape no longer runs or prints
    ''' the submission name (C# scripting parity, D7 revert). The keyword is rejected at binding, before member lookup
    ''' of the 'ToString' would ever apply.
    ''' </summary>
    <Fact>
    Public Sub TopLevelMyBaseInMethodBody_IsReported()
        ScriptModeConformance.AssertReports(
            "Function Describe() As String" & vbCrLf &
            "    Return MyBase.ToString()" & vbCrLf &
            "End Function" & vbCrLf &
            "Return Describe()", "BC36966")
    End Sub

#End Region

#Region "Options"

    ''' <summary>Option Strict On really applies to the script compilation: the implicit narrowing is BC30512.</summary>
    <Fact>
    Public Sub OptionStrictOn_TakesEffect()
        ScriptModeConformance.AssertReports(
            "Option Strict On" & vbCrLf &
            "Dim boxed As Object = 1" & vbCrLf &
            "Dim unboxed As Integer = boxed" & vbCrLf &
            "Return unboxed", "BC30512")
    End Sub

    ''' <summary>Option Strict Off (the default) allows the same conversion, at run time.</summary>
    <Fact>
    Public Sub OptionStrictOff_Conforms()
        ScriptModeConformance.AssertRuns(
            "Option Strict Off" & vbCrLf &
            "Dim boxed As Object = 1" & vbCrLf &
            "Dim unboxed As Integer = boxed" & vbCrLf &
            "Return unboxed", 1)
    End Sub

    ''' <summary>Option Compare Text makes the comparison case insensitive.</summary>
    <Fact>
    Public Sub OptionCompareText_TakesEffect()
        ScriptModeConformance.AssertRuns(
            "Option Compare Text" & vbCrLf &
            "Dim answer As String = ""Binary""" & vbCrLf &
            "If ""A"" = ""a"" Then answer = ""Text""" & vbCrLf &
            "Return answer", "Text")
    End Sub

    ''' <summary>The default is Option Compare Binary.</summary>
    <Fact>
    Public Sub OptionCompareBinaryDefault_Conforms()
        ScriptModeConformance.AssertRuns(
            "Dim answer As String = ""Binary""" & vbCrLf &
            "If ""A"" = ""a"" Then answer = ""Text""" & vbCrLf &
            "Return answer", "Binary")
    End Sub

    ''' <summary>Option Infer Off makes a Dim with no As clause an Object inside a method body.</summary>
    <Fact>
    Public Sub OptionInferOff_MakesLocalObject_Conforms()
        ScriptModeConformance.AssertRuns(
            "Option Infer Off" & vbCrLf &
            "Dim answer As String = """"" & vbCrLf &
            "Function Tell(o As Object) As String" & vbCrLf &
            "    Return ""object""" & vbCrLf &
            "End Function" & vbCrLf &
            "Function Tell(i As Integer) As String" & vbCrLf &
            "    Return ""integer""" & vbCrLf &
            "End Function" & vbCrLf &
            "Sub Probe()" & vbCrLf &
            "    Dim inferred = 1" & vbCrLf &
            "    answer = Tell(inferred)" & vbCrLf &
            "End Sub" & vbCrLf &
            "Probe()" & vbCrLf &
            "Return answer", "object")
    End Sub

    ''' <summary>Infer is on by default, so the same local is an Integer.</summary>
    <Fact>
    Public Sub OptionInferDefault_MakesLocalInteger_Conforms()
        ScriptModeConformance.AssertRuns(
            "Dim answer As String = """"" & vbCrLf &
            "Function Tell(o As Object) As String" & vbCrLf &
            "    Return ""object""" & vbCrLf &
            "End Function" & vbCrLf &
            "Function Tell(i As Integer) As String" & vbCrLf &
            "    Return ""integer""" & vbCrLf &
            "End Function" & vbCrLf &
            "Sub Probe()" & vbCrLf &
            "    Dim inferred = 1" & vbCrLf &
            "    answer = Tell(inferred)" & vbCrLf &
            "End Sub" & vbCrLf &
            "Probe()" & vbCrLf &
            "Return answer", "integer")
    End Sub

    ''' <summary>
    ''' Issue 32: the same 'Dim x = &lt;expr&gt;' at the top level now infers (Option Infer On), so the field's
    ''' static type is Integer and the overload pair resolves to the Integer arm - identical to the method-body
    ''' local control above. Before the fix this cell asserted "object" (the registered divergence).
    ''' </summary>
    <Fact>
    Public Sub TopLevelInferredField_Infers_Conforms()
        ScriptModeConformance.AssertRuns(
            "Option Infer On" & vbCrLf &
            "Dim answer As String = """"" & vbCrLf &
            "Function Tell(o As Object) As String" & vbCrLf &
            "    Return ""object""" & vbCrLf &
            "End Function" & vbCrLf &
            "Function Tell(i As Integer) As String" & vbCrLf &
            "    Return ""integer""" & vbCrLf &
            "End Function" & vbCrLf &
            "Dim inferred = 1" & vbCrLf &
            "answer = Tell(inferred)" & vbCrLf &
            "Return answer", "integer")
    End Sub

    ''' <summary>
    ''' Issue 32: with Option Strict On but Option Infer still On (the default), a top level 'Dim inferred = 1'
    ''' infers Integer and is legal - matching an ordinary local (§七 row 1) - so it returns 1 instead of the old
    ''' BC30209. (Option Infer Off + Strict On still reports BC30209; see ScriptTopLevelDimInferenceTests.T7.)
    ''' </summary>
    <Fact>
    Public Sub TopLevelInferredFieldWithStrictOn_Infers()
        ScriptModeConformance.AssertRuns(
            "Option Strict On" & vbCrLf &
            "Dim inferred = 1" & vbCrLf &
            "Return inferred", 1)
    End Sub

    ''' <summary>Option Explicit Off still declares an implicit local inside a method body.</summary>
    <Fact>
    Public Sub OptionExplicitOff_ConformsInMethodBody()
        ScriptModeConformance.AssertRuns(
            "Option Explicit Off" & vbCrLf &
            "Dim answer As String = ""none""" & vbCrLf &
            "Sub Probe()" & vbCrLf &
            "    undeclared = 5" & vbCrLf &
            "    answer = undeclared.ToString()" & vbCrLf &
            "End Sub" & vbCrLf &
            "Probe()" & vbCrLf &
            "Return answer", "5")
    End Sub

    ''' <summary>
    ''' The top level counterpart: an undeclared name is still BC30451, Option Explicit Off does not rescue it -
    ''' top level statements cannot introduce implicit locals.
    ''' </summary>
    <Fact>
    Public Sub TopLevelUndeclaredName_IsReported()
        ScriptModeConformance.AssertReports(
            "Option Explicit Off" & vbCrLf &
            "undeclared = 5" & vbCrLf &
            "Return undeclared", "BC30451")
    End Sub

    ''' <summary>An Option statement has to precede the Imports statements and every other statement: BC30627.</summary>
    <Fact>
    Public Sub OptionStatementAfterImport_IsReported()
        ScriptModeConformance.AssertReports(
            "Imports System.Text" & vbCrLf &
            "Option Strict On" & vbCrLf &
            "Return ""OPTS""", "BC30627")
    End Sub

    <Fact>
    Public Sub OptionStatementAfterStatement_IsReported()
        ScriptModeConformance.AssertReports(
            "System.Console.WriteLine(1)" & vbCrLf &
            "Option Infer On" & vbCrLf &
            "Return ""OPTS""", "BC30627")
    End Sub

#End Region

#Region "Error and Resume statements"

    ''' <summary>
    ''' <c>Error n</c> at the top level. The statement raises the VB error the number names, and it raises it as an
    ''' ordinary .NET exception, so the top level <c>Try</c> around it catches it and the statements after
    ''' <c>End Try</c> still run - the cell pins both halves (the raise and the survival) in one value, because an
    ''' uncaught top level <c>Error n</c> leaves the submission without a return value at all.
    ''' </summary>
    <Fact>
    Public Sub TopLevelErrorStatement_IsCaughtByTheEnclosingTry()
        ScriptModeConformance.AssertRuns(
            "Dim state As Integer = 0" & vbCrLf &
            "Try" & vbCrLf &
            "    Error 5" & vbCrLf &
            "Catch ex As System.Exception" & vbCrLf &
            "    state += 1" & vbCrLf &
            "End Try" & vbCrLf &
            "state += 10" & vbCrLf &
            "Return state", 11)
    End Sub

    ''' <summary>
    ''' A bare <c>Resume</c> at the top level: its host is the async script initializer, which is not an error
    ''' handling method, so BC36956 is reported. The same statement inside a top level <c>Sub</c> is ordinary VB
    ''' (the <c>OnErrorInsideMethod_Conforms</c> cell above covers the handler it needs).
    ''' </summary>
    <Fact>
    Public Sub TopLevelResumeStatement_IsReported()
        ScriptModeConformance.AssertReports("Resume", "BC36956")
    End Sub

    ''' <summary>The <c>Resume Next</c> form of the same statement, rejected the same way.</summary>
    <Fact>
    Public Sub TopLevelResumeNextStatement_IsReported()
        ScriptModeConformance.AssertReports("Resume Next", "BC36956")
    End Sub

#End Region

#Region "Member declaration forms"

    ''' <summary>
    ''' <c>Optional</c> parameters on a <c>Function</c> the submission class declares. The call site is the
    ''' discriminator: <c>Tell()</c> takes both defaults, <c>Tell(2)</c> overrides only the first, and
    ''' <c>Tell(, 3)</c> overrides only the second, so all three signatures of the one declaration have to be
    ''' built with the right default per parameter.
    ''' </summary>
    <Fact>
    Public Sub TopLevelOptionalParameters_Conform()
        ScriptModeConformance.AssertRuns(
            "Function Tell(Optional x As Integer = 5, Optional y As Integer = 8) As String" & vbCrLf &
            "    Return x & "","" & y" & vbCrLf &
            "End Function" & vbCrLf &
            "Return Tell() & ""/"" & Tell(2) & ""/"" & Tell(, 3)", "5,8/2,8/5,3")
    End Sub

    ''' <summary>
    ''' A <c>ParamArray</c> parameter on a top level <c>Function</c>. The three call shapes are the ones the
    ''' modifier exists for: no argument (an empty array is synthesized), one argument, and several.
    ''' </summary>
    <Fact>
    Public Sub TopLevelParamArrayParameters_Conform()
        ScriptModeConformance.AssertRuns(
            "Function SumAll(ParamArray items As Integer()) As Integer" & vbCrLf &
            "    Dim total As Integer = 0" & vbCrLf &
            "    For Each item In items" & vbCrLf &
            "        total += item" & vbCrLf &
            "    Next" & vbCrLf &
            "    Return total" & vbCrLf &
            "End Function" & vbCrLf &
            "Return SumAll() & ""/"" & SumAll(1) & ""/"" & SumAll(1, 2, 3)", "0/1/6")
    End Sub

    ''' <summary>
    ''' A <c>Partial</c> method split over two <c>Partial Class</c> declarations of the submission class. The
    ''' discriminator is that the body declared in the second declaration is the one the call in the first
    ''' declaration reaches: the signature only, or a call left unmerged, answers <c>False</c>.
    ''' </summary>
    <Fact>
    Public Sub TopLevelPartialMethods_Conform()
        ScriptModeConformance.AssertRuns(
            "Partial Class Part" & vbCrLf &
            "    Private _built As Boolean" & vbCrLf &
            "    Partial Private Sub OnBuild()" & vbCrLf &
            "    End Sub" & vbCrLf &
            "    Public Sub Go()" & vbCrLf &
            "        OnBuild()" & vbCrLf &
            "    End Sub" & vbCrLf &
            "    Public Function Built() As Boolean" & vbCrLf &
            "        Return _built" & vbCrLf &
            "    End Function" & vbCrLf &
            "End Class" & vbCrLf &
            "Partial Class Part" & vbCrLf &
            "    Private Sub OnBuild()" & vbCrLf &
            "        _built = True" & vbCrLf &
            "    End Sub" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim made As New Part" & vbCrLf &
            "made.Go()" & vbCrLf &
            "Return made.Built()", True)
    End Sub

    ''' <summary>
    ''' An <c>Iterator</c> property declared by a type the submission class nests, consumed by a top level
    ''' <c>For Each</c>. The sum is over three <c>Yield</c> statements, so the property access has to produce the
    ''' state machine the <c>Iterator</c> modifier asks for rather than a single value.
    ''' </summary>
    <Fact>
    Public Sub TopLevelIteratorProperty_Conform()
        ScriptModeConformance.AssertRuns(
            "Class Bag" & vbCrLf &
            "    Iterator ReadOnly Property Items As System.Collections.Generic.IEnumerable(Of Integer)" & vbCrLf &
            "        Get" & vbCrLf &
            "            Yield 1" & vbCrLf &
            "            Yield 2" & vbCrLf &
            "            Yield 3" & vbCrLf &
            "        End Get" & vbCrLf &
            "    End Property" & vbCrLf &
            "End Class" & vbCrLf &
            "Dim held As New Bag" & vbCrLf &
            "Dim total As Integer = 0" & vbCrLf &
            "For Each value In held.Items" & vbCrLf &
            "    total += value" & vbCrLf &
            "Next" & vbCrLf &
            "Return total", 6)
    End Sub

#End Region

End Class
