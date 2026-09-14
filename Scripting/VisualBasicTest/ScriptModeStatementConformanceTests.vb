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
    ''' An explicit 'Me' is not valid in script code: BC36966
    ''' (<c>Binder_Expressions.vb:2257</c>, 'Me/MyClass/MyBase implicitly but not explicitly').
    ''' </summary>
    <Fact>
    Public Sub TopLevelExplicitMe_IsReported()
        ScriptModeConformance.AssertReports("Return Me.ToString()", "BC36966")
    End Sub

    ''' <summary>
    ''' The surprising position: an explicit Me inside the body of a member the submission class declares itself -
    ''' the whole script class is a script class, so the body is covered by the same rule.
    ''' </summary>
    <Fact>
    Public Sub TopLevelExplicitMeInMethodBody_IsReported()
        ScriptModeConformance.AssertReports(
            "Function Describe() As String" & vbCrLf &
            "    Return Me.ToString()" & vbCrLf &
            "End Function" & vbCrLf &
            "Return Describe()", "BC36966")
    End Sub

    ''' <summary>The same rule for an explicit MyClass.</summary>
    <Fact>
    Public Sub TopLevelExplicitMyClass_IsReported()
        ScriptModeConformance.AssertReports(
            "Function Describe() As String" & vbCrLf &
            "    Return MyClass.ToString()" & vbCrLf &
            "End Function" & vbCrLf &
            "Return Describe()", "BC36966")
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
    ''' The same 'Dim x = &lt;expr&gt;' at the top level does not infer, even with Option Infer On: the submission
    ''' field is bound as Object (the overload of the Object parameter wins). Option Strict On reports BC30209 for
    ''' the declaration, which is the same 'no As clause' diagnostic an inferred local would not get.
    ''' </summary>
    <Fact>
    Public Sub TopLevelInferredField_IsObject_Conforms()
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
            "Return answer", "object")
    End Sub

    ''' <summary>A top level 'Dim x = 1' has no As clause as far as Option Strict On is concerned: BC30209.</summary>
    <Fact>
    Public Sub TopLevelInferredFieldWithStrictOn_IsReported()
        ScriptModeConformance.AssertReports(
            "Option Strict On" & vbCrLf &
            "Dim inferred = 1" & vbCrLf &
            "Return inferred", "BC30209")
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

End Class
