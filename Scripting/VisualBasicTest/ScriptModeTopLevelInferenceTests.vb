' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System
Imports System.Linq
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Scripting
Imports Microsoft.CodeAnalysis.VisualBasic.Scripting
Imports Xunit

''' <summary>
''' What follows from "a top level <c>Dim</c> without an <c>As</c> clause is bound as <c>Object</c>"
''' (design-detailed.md §U1; the behaviour itself is already pinned by
''' <c>ScriptModeStatementConformanceTests.TopLevelInferredField_IsObject_Conforms</c> and
''' <c>TopLevelInferredFieldWithStrictOn_IsReported</c>).
''' <para>
''' The initial A1 report was "a top level LINQ Group By query throws <c>InvalidCastException</c>", i.e. a
''' suspected compiler defect. It is not one: the query is legal, the top level field that holds it is not
''' inferred, and the field being <c>Object</c> forces the follow up member access through the late binder, which
''' is where the exception comes from (design-overview.md §3). The cases below pin both halves of that chain so
''' the consequence stops being an accidental probe finding.
''' </para>
''' <para>
''' The discriminator for "was the field inferred?" is <b>overload resolution</b>, never <c>GetType()</c>:
''' a late bound <c>Object.GetType()</c> answers with the runtime type, so an <c>Object</c> field and an inferred
''' field reply identically and the probe has no discriminating power (design-detailed.md §1.2 item 4). The pair
''' <c>Tell(o As Object)</c> / <c>Tell(items As IEnumerable)</c> answers differently for the two static types -
''' the <c>IEnumerable</c> parameter is the more specific of two widening reference conversions, so it wins only
''' when the argument really is a sequence. <c>SubLocalQuery_IsSequence</c> is the control that proves the pair
''' is sensitive to inference at all; without it a blanket "object" answer would look like a pass.
''' </para>
''' <para>
''' No file is written, no process is started, and there is no registry or network access. The one assembly
''' reference these cases add is resolved from a path that already exists on disk and opened read-only by the
''' metadata layer.
''' </para>
''' </summary>
Public Class ScriptModeTopLevelInferenceTests

    ''' <summary>
    ''' <see cref="ScriptModeConformance.DefaultOptions"/> carries only the scripting defaults, and the query
    ''' constructors (<c>Select</c>/<c>Where</c>/<c>GroupBy</c>) plus <c>IGrouping</c> live in System.Linq, which
    ''' those defaults do not reference - without the reference a query reports BC36593 and nothing else. No file is
    ''' written: the reference comes from an assembly that is already on disk, which the metadata layer opens
    ''' read-only (<c>MetadataReference.CreateFromFile</c>).
    ''' </summary>
    Private Shared ReadOnly WithLinqOptions As ScriptOptions =
        ScriptModeConformance.DefaultOptions.
            AddReferences(GetType(Enumerable).Assembly).
            AddImports("System.Linq")

    ''' <summary>
    ''' The shape the A1 probes used (tmp\probes\u14\r5\decisive.py): group words by length, project
    ''' (length, count). Over <c>{"aa", "b", "ccc", "dd"}</c> the groups are 2:2, 1:1, 3:1 in first appearance
    ''' order. The same text is used by every cell, so the cells differ only in where the declaration lives and in
    ''' whether it carries an <c>As</c> clause. The projection is anonymous, so the element type has no name that
    ''' an <c>As</c> clause could spell; the cell that needs a typed declaration names the untyped
    ''' <c>IEnumerable</c> instead and reads the elements through it.
    ''' </summary>
    Private Const GroupingQuery As String =
        "From w In words Group By k = w.Length Into g = Group Select k, c = g.Count()"

    Private Const Words As String = "Dim words As String() = {""aa"", ""b"", ""ccc"", ""dd""}"

    Private Const ExpectedGroups As String = "2:2,1:1,3:1"

    ''' <summary>
    ''' The overload pair that reads the static type of its argument: an <c>Object</c> argument takes the identity
    ''' conversion to the <c>Object</c> parameter, a sequence argument takes the more specific widening to
    ''' <c>IEnumerable</c>. Under the default <c>Option Strict Off</c> the <c>Object</c> to <c>IEnumerable</c> route
    ''' would be a narrowing conversion, so it never wins by accident.
    ''' </summary>
    Private Const TellOverloads As String =
        "Function Tell(o As Object) As String" & vbCrLf &
        "    Return ""object""" & vbCrLf &
        "End Function" & vbCrLf &
        "Function Tell(items As System.Collections.IEnumerable) As String" & vbCrLf &
        "    Return ""sequence""" & vbCrLf &
        "End Function"

    ' ---- (1) the top level field holds an Object ----

    ''' <summary>
    ''' Cell one of the pair: a top level <c>Dim q = &lt;query&gt;</c> with no <c>As</c> clause keeps the static
    ''' type <c>Object</c>, and <c>Option Infer On</c> does not change that. The assertion is the overload
    ''' resolution result, not the runtime type of the value.
    ''' </summary>
    <Fact>
    Public Sub TopLevelQueryField_IsObject()
        ScriptModeConformance.AssertRuns(
            "Option Infer On" & vbCrLf &
            "Dim answer As String = """"" & vbCrLf &
            TellOverloads & vbCrLf &
            Words & vbCrLf &
            "Dim q = " & GroupingQuery & vbCrLf &
            "answer = Tell(q)" & vbCrLf &
            "Return answer", "object", WithLinqOptions)
    End Sub

    ' ---- (2) the same query infers and evaluates when it is not a top level field ----

    ''' <summary>
    ''' Control for the overload pair: the very same query in a local of a top level <c>Sub</c> <b>is</b> inferred,
    ''' and the pair answers "sequence" there. This is what makes the "object" reading above discriminating - if
    ''' both cells returned "object" the pair would be insensitive to inference and the first cell would prove
    ''' nothing (design-detailed.md §1.4).
    ''' </summary>
    <Fact>
    Public Sub SubLocalQuery_IsSequence()
        ScriptModeConformance.AssertRuns(
            "Option Infer On" & vbCrLf &
            "Dim answer As String = """"" & vbCrLf &
            TellOverloads & vbCrLf &
            "Sub Probe()" & vbCrLf &
            "    " & Words & vbCrLf &
            "    Dim q = " & GroupingQuery & vbCrLf &
            "    answer = Tell(q)" & vbCrLf &
            "End Sub" & vbCrLf &
            "Probe()" & vbCrLf &
            "Return answer", "sequence", WithLinqOptions)
    End Sub

    ''' <summary>
    ''' Cell two of the pair: the same query in the same container runs and produces the right groups. The
    ''' container of the declaration is the only difference to the first cell, so the failure reported for the top
    ''' level shape is about where the declaration lives, not about the query being illegal.
    ''' </summary>
    <Fact>
    Public Sub SubLocalQuery_Evaluates()
        ScriptModeConformance.AssertRuns(
            "Option Infer On" & vbCrLf &
            "Dim answer As String = """"" & vbCrLf &
            "Sub Probe()" & vbCrLf &
            "    " & Words & vbCrLf &
            "    Dim q = " & GroupingQuery & vbCrLf &
            "    answer = String.Join("","", q.Select(Function(x) x.k & "":"" & x.c))" & vbCrLf &
            "End Sub" & vbCrLf &
            "Probe()" & vbCrLf &
            "Return answer", ExpectedGroups, WithLinqOptions)
    End Sub

    ''' <summary>
    ''' The other escape hatch named by the design: the same query text in the same top level position, with the
    ''' one difference that the declaration gets an <c>As</c> clause, so the field's static type is the sequence
    ''' instead of <c>Object</c> and the member call after it binds early. That single clause is the whole
    ''' difference to <see cref="TopLevelQueryField_IsObject"/> and to
    ''' <see cref="TopLevelQueryField_LateBoundSelect_Fails"/>. The clause names the untyped <c>IEnumerable</c>, not
    ''' the query's element type, which is exactly the type the compiler would otherwise have to infer - the
    ''' anonymous projection has no name to write there.
    ''' </summary>
    <Fact>
    Public Sub TopLevelQueryWithAsClause_Evaluates()
        ScriptModeConformance.AssertRuns(
            Words & vbCrLf &
            "Dim q As System.Collections.IEnumerable = " & GroupingQuery & vbCrLf &
            "Return String.Join("","", q.Cast(Of Object)().Select(Function(x) x.k & "":"" & x.c))",
            ExpectedGroups, WithLinqOptions)
    End Sub

    ' ---- the negative cell: what the Object field costs at run time ----

    ''' <summary>
    ''' Cell three of the pair, negative: following the <c>Object</c> typed field with a member call reports a
    ''' failure rather than "no exception" - the front end is clean, so the exception is the late binder refusing
    ''' to resolve the member on the runtime shape. Observed form: <c>InvalidCastException</c> whose message is
    ''' "Overload resolution failed because no Public 'Select' can be called with these arguments: ...". Only the
    ''' member name is asserted - it is the part that ties the failure to this cell, and it does not move with the
    ''' resource culture the way the surrounding sentence does.
    ''' </summary>
    <Fact>
    Public Sub TopLevelQueryField_LateBoundSelect_Fails()
        Dim failure = RunExpectingFailure(
            Words & vbCrLf &
            "Dim q = " & GroupingQuery & vbCrLf &
            "Return q.Select(Function(x) x.k & "":"" & x.c).Count()")

        Assert.IsType(Of InvalidCastException)(failure)
        Assert.Contains("Select", failure.Message)
    End Sub

    ''' <summary>
    ''' Runs a submission that is expected to pass the front end and fail while running, and hands the exception
    ''' the host sees back to the cell. Compiling clean is asserted here because it is the whole point: the failure
    ''' is not a rejected submission, and <c>CompilationErrorException</c> would be the wrong shape.
    ''' </summary>
    Private Shared Function RunExpectingFailure(source As String) As Exception
        Dim script = VisualBasicScript.Create(source, WithLinqOptions)
        Dim errors = script.Compile().Where(Function(d) d.Severity = DiagnosticSeverity.Error).ToArray()

        Assert.True(errors.Length = 0,
                    "the submission has to compile; it did not: " &
                    String.Join(", ", errors.Select(Function(d) d.Id)) & vbCrLf & source)

        Try
            script.RunAsync().GetAwaiter().GetResult()
        Catch ex As Exception
            Return ex
        End Try

        Assert.True(False, "the submission was expected to fail at run time" & vbCrLf & source)
        Return Nothing
    End Function

End Class
